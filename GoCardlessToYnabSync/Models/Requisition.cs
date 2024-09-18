using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GoCardlessToYnabSync.Models
{
    public class Requisition
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty(PropertyName = "requisitionId")]
        public string RequisitionId { get; set; } = null!;

        [JsonProperty(PropertyName = "createdOn")]
        public DateTime CreatedOn { get; set; }

        [JsonProperty(PropertyName = "lastSyncOn")]
        public DateTime? LastSyncOn { get; set; }

        [JsonProperty(PropertyName = "valid")]
        public bool? Valid { get; set; }
    }
}
