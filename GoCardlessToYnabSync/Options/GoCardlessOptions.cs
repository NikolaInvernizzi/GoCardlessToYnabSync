using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GoCardlessToYnabSync.Options
{
    public class GoCardlessOptions
    {
        public const string GoCardless = "GoCardless";
        public string SecretId { get; set; } = String.Empty;
        public string Secret { get; set; } = String.Empty;
        public string BankId { get; set; } = String.Empty;
        public int DaysInPastToRetrieve { get; set; } = 7;
    }
}
