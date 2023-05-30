using Microsoft.EntityFrameworkCore;

namespace logipage_api.Models
{
    public class MyDbContext : DbContext
    {
        public MyDbContext(DbContextOptions<MyDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<Role> Roles { get; set; }
        public DbSet<UserRole> UserRoles { get; set; }
        public DbSet<SuccessLog> SuccessLogin { get; set; }
        public DbSet<ErrorLog> ErrorLogin { get; set; }
    }
}