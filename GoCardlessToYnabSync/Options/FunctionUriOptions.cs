using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GoCardlessToYnabSync.Options
{
    public class FunctionUriOptions
    {
        public const string FunctionUris = "FunctionUris";
        public string GoCardlessSync { get; set; } = String.Empty;
        public string YnabSync { get; set; } = String.Empty;
    }
}
