using Newtonsoft.Json;
using RobinTTY.NordigenApiClient.Models.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GoCardlessToYnabSync.Models
{

    public class Transaction
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = null!;

        [JsonProperty(PropertyName = "entryReference")]
        public string EntryReference { get; set; } = null!;

        [JsonProperty(PropertyName = "bookingDate")]
        public DateTime BookingDate { get; set; }

        [JsonProperty(PropertyName = "syncedOn")]
        public DateTime? SyncedOn { get; set; } = null;

        [JsonProperty(PropertyName = "transaction")]
        public RobinTTY.NordigenApiClient.Models.Responses.Transaction TransactionObject { get; set; } = null!;
    }
}
