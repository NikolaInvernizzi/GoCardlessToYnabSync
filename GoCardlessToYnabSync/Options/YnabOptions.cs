using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GoCardlessToYnabSync.Options
{
    public class YnabOptions
    {
        public const string Ynab = "Ynab";
        public string Secret { get; set; } = String.Empty;
        public string BudgetId { get; set; } = String.Empty;
        public string AccountName { get; set; } = String.Empty;
    }
}
