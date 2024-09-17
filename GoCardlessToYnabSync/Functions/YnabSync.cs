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

        public YnabSync(ILogger<YnabSync> logger, YnabSyncService ynabSyncService, MailService mailService)
        {
            _logger = logger;
            _ynabSyncService = ynabSyncService;
            _mailService = mailService;
        }

        [Function("YnabSync")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {
            _logger.LogInformation("YnabSync HTTP function triggered");

            var ynabSyncResult = await _ynabSyncService.PushTransactionsToYnab();

            if (int.TryParse(ynabSyncResult, out var count) && count > 0)
            {
                _mailService.SendMail($"{count} items have been synced to ynab and need to be categorized.", $"{count} items synced to ynab");
            }

            return new OkObjectResult($"Result from pushing: {ynabSyncResult}");
        }
    }
}
