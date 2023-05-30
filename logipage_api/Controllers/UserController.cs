using logipage_api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
namespace logipage_api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserController : ControllerBase
    {
        private readonly MyDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly IConnectionMultiplexer _redisConnection;

        public UserController(MyDbContext context, IConfiguration configuration, IHttpClientFactory httpClientFactory,IMemoryCache cache, IConnectionMultiplexer redisConnection)
        {
            _context = context;
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient();
            _cache = cache;
            _redisConnection = redisConnection;

        }

        [HttpPost]
        [AllowAnonymous]
        [Route("authenticate")]
        public async Task<IActionResult> Authenticate([FromBody] User user)
        {
            DateTime dt = DateTime.Now;
            //TRİM VE LOWERCASE DÖNÜŞÜMÜ
            var trimmedUsername = user.Username.Trim().ToLower();
            var trimmedAd = user.Ad.Trim().ToLower();
            Console.WriteLine(trimmedAd+"trimmed");
            var dbUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == trimmedUsername && u.Ad == trimmedAd);

            var userRoles = await (from u in _context.Users
                                   join ur in _context.UserRoles on u.UserId equals ur.UserId
                                   join r in _context.Roles on ur.RoleId equals r.RoleId
                                   where u.Username == user.Username && u.Ad == user.Ad
                                   select r.RoleName.Trim()).ToListAsync();
            Console.WriteLine($"Kullanıcının rolleri: {string.Join(", ", userRoles)}");
            //string a = u.Ad.Trim();
            // Console.WriteLine(a+":   a'nın değeri");
            //GetSuccessLoginData(a);
            Console.WriteLine("rememberme değeri:"+user.RememberMe);

            if (user.RememberMe)
            {
                var redisDatabase = _redisConnection.GetDatabase();
                await redisDatabase.StringSetAsync("rememberedSicil", user.Username);
                await redisDatabase.StringSetAsync("rememberedName", user.Ad);
                await redisDatabase.StringSetAsync("rememberMe", user.RememberMe);
            }

            if (dbUser == null)
            {
                // Eğer kullanıcı yoksa ErrorLogin tablosuna kaydet
                var errorLogin = new ErrorLog
                {
                    Ad = user.Ad,
                    LoginDurumu = 0,
                    LoginTarihi = dt.ToString(),
                };
                await _context.ErrorLogin.AddAsync(errorLogin);
                await _context.SaveChangesAsync();

                return Unauthorized();
            }
            else
            {
                _cache.Set("LastLoggedInUser", user.Ad);
                // Kullanıcı varsa SuccessLogin tablosuna kaydet
                var successLogin = new SuccessLog
                {
                    Ad = user.Ad,
                    LoginDurumu = 1,
                    LoginTarihi = dt.ToString(),
                };
                await _context.SuccessLogin.AddAsync(successLogin);
                await _context.SaveChangesAsync();

                var tokenHandler = new JwtSecurityTokenHandler();
                var key = Encoding.ASCII.GetBytes(_configuration["Jwt:SecretKey"]);
                if (key.Length < 128 / 8) // check key length and generate a new key if necessary
                {
                    using var rng = new RNGCryptoServiceProvider();
                    var keyBytes = new byte[128 / 8];
                    rng.GetBytes(keyBytes);
                    key = keyBytes;
                }
                var tokenDescriptor = new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity(new Claim[]
                    {
                    new Claim(ClaimTypes.Name, dbUser.Username.Trim()),
                    new Claim(ClaimTypes.Email, dbUser.Ad.Trim()),
                    }.Concat(userRoles.Select(role => new Claim(ClaimTypes.Role, role)))), // kullanıcının rollerini ekler
                    Expires = DateTime.UtcNow.AddDays(7),
                    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
                };
                var token = tokenHandler.CreateToken(tokenDescriptor);

                return Ok(new
                {
                    Token = tokenHandler.WriteToken(token),
                    Roles = userRoles
                });
            }

        }
        [HttpGet("userinfo")]
        public IActionResult GetUserInfo()
        {
            var redisDatabase = _redisConnection.GetDatabase();
            var rememberedSicil = redisDatabase.StringGet("rememberedSicil");
            var rememberedName = redisDatabase.StringGet("rememberedName");
            var rememberMe = redisDatabase.StringGet("rememberMe");

            var userInfo = new User
            {
                Username = rememberedSicil,
                Ad = rememberedName,
                RememberMe = (bool)rememberMe
            };

            return Ok(userInfo);
        }
        [HttpGet]
        [Route("successlogin")]
        public async Task<IActionResult> GetSuccessLoginData()
        {
            try
            {
                var lastLoggedInUser = _cache.Get<string>("LastLoggedInUser");
                var elasticUrl = "http://10.12.149.11:9200/successlogin/_search";
                var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("elastic:2021cj"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                var query = new
                {
                    query = new
                    {
                        match_phrase = new
                        {
                            ad = lastLoggedInUser
                        }
                    }
                };

                Console.WriteLine(lastLoggedInUser+"query'den sonraki ilk cw");
                var jsonQuery = JsonConvert.SerializeObject(query);
                var content = new StringContent(jsonQuery, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(elasticUrl, content);
                response.EnsureSuccessStatusCode();

                var successLoginData = await response.Content.ReadAsStringAsync();

                return Ok(successLoginData);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Elasticsearch verileri alınırken bir hata oluştu: " + ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }


        [HttpGet]
        [Route("errorlogin")]
        public async Task<IActionResult> GetErrorLoginData()
        {
            try
            {
                var lastLoggedInUser = _cache.Get<string>("LastLoggedInUser");
                var elasticUrl = "http://10.12.149.11:9200/errorlogin/_search";
                var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("elastic:2021cj"));
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

                var query = new
                {
                    query = new
                    {
                        match = new
                        {
                            ad = lastLoggedInUser
                        }
                    }
                };

                var jsonQuery = JsonConvert.SerializeObject(query);
                var content = new StringContent(jsonQuery, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(elasticUrl, content);
                response.EnsureSuccessStatusCode();

                var successLoginData = await response.Content.ReadAsStringAsync();

                return Ok(successLoginData);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Elasticsearch verileri alınırken bir hata oluştu: " + ex.Message);
                return StatusCode(500, "Internal Server Error");
            }
        }

        [HttpGet]
        [Route("dd")]
        public async Task<IActionResult> GetUser()
        {
            var users = _context.Users.ToList();
            return Ok(users);
        }
    }
}
