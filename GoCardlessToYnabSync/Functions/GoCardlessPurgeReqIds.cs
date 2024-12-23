using GoCardlessToYnabSync.Options;
using GoCardlessToYnabSync.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoCardlessToYnabSync.Functions
{
    public class GoCardlessPurgeReqIds
    {
        private readonly ILogger<GoCardlessSync> _logger;
        private readonly GoCardlessSyncService _goCardlessSyncService;
        private readonly MailService _mailService;

        public GoCardlessPurgeReqIds(ILogger<GoCardlessSync> logger, GoCardlessSyncService goCardlessSyncService, MailService mailService)
        {
            _logger = logger;
            _goCardlessSyncService = goCardlessSyncService;
            _mailService = mailService;
        }

        [Function("GoCardlessPurgeRequisitionIds")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {
            _logger.LogInformation("GoCardless Purge Requisition Ids HTTP function triggered");

            string goCardlessResult;
            try
            {
                var goCardlessSyncResult = await _goCardlessSyncService.PurgeGoCardlessRequisitionIds();
                goCardlessResult = $"Purge GoCardless Requisition Ids result:\t{goCardlessSyncResult}";
            }
            catch (Exception ex)
            {
                goCardlessResult = $"Purge GoCardless Requisition Ids result:\t{ex.Message}";
            }       

            return new OkObjectResult($"{goCardlessResult}");
        }
    }
}
