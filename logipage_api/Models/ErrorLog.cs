using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace logipage_api.Models
{
    public class ErrorLog
    {
        public int Id { get; set; }
        public string Ad { get; set; }
        public int LoginDurumu { get; set; }
        public String LoginTarihi { get; set; }
    }
}
