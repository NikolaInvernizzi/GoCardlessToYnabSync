using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GoCardlessToYnabSync.Options
{
    public class SmptOptions
    {
        public const string Smpt = "Smpt";
        public string Host { get; set; } = String.Empty;
        public int Port { get; set; } = 0;
        public string Password { get; set; } = String.Empty;
        public string Email { get; set; } = String.Empty;

        public string SendTo { get; set; } = String.Empty;
    }
}
