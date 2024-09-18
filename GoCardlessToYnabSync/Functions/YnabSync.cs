using GoCardlessToYnabSync.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace GoCardlessToYnabSync.Functions
{
    public class YnabSync
    {
        private readonly ILogger<YnabSync> _logger;
        private readonly YnabSyncService _ynabSyncService;
        private readonly MailService _mailService;

        public YnabSync(ILogger<YnabSync> logger, YnabSyncService ynabSyncService)
        {
            _logger = logger;
            _ynabSyncService = ynabSyncService;
        }

        [Function("YnabSync")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {
            _logger.LogInformation("YnabSync HTTP function triggered");

            try
            {
                var ynabSyncResult = await _ynabSyncService.SyncToYnab();
                return new OkObjectResult($"Result from pushing:\t{ynabSyncResult} items pushed to Ynab");
            }
            catch (Exception ex)
            {
                return new OkObjectResult($"Result from pushing:\t{ex.Message}");
            }
        }
    }
}
