using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GoCardlessToYnabSync.Options
{
    public class CosmosDbOptions
    {
        public const string CosmosDb = "CosmosDb";
        public string ConnectionString { get; set; } = String.Empty;
        public string Database { get; set; } = String.Empty;
        public string ContainerTransactions { get; set; } = String.Empty;
        public string ContainerTransactionsPartitionKey { get; set; } = String.Empty;
        public string ContainerRequisitions { get; set; } = String.Empty;
        public string ContainerRequisitionsPartitionKey { get; set; } = String.Empty;
    }
}
