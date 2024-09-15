using GoCardlessToYnabSync.Options;
using GoCardlessToYnabSync.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoCardlessToYnabSync.Functions
{
    public class GoCardlessSync
    {
        private readonly ILogger<GoCardlessSync> _logger;
        private readonly IConfiguration _configuration;
        private readonly GoCardlessSyncService _goCardlessSyncService;
        private readonly MailService _mailService;

        public GoCardlessSync(ILogger<GoCardlessSync> logger, IConfiguration configuration, GoCardlessSyncService goCardlessSyncService, MailService mailService)
        {
            _logger = logger;
            _configuration = configuration;
            _goCardlessSyncService = goCardlessSyncService;
            _mailService = mailService;
        }

        [Function("GoCardlessSync")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {
            _logger.LogInformation("GoCardlessSync HTTP function triggered");

            var goCardlessSyncResult = await _goCardlessSyncService.PullTransactionsFromGoCardless();

            var functionUris = new FunctionUriOptions();
            _configuration.GetSection(FunctionUriOptions.FunctionUris).Bind(functionUris);

            var ynabClient = new HttpClient();
            var ynabClientResult = await ynabClient.GetAsync(functionUris.YnabSync);
            var ynabSyncResultContent = await ynabClientResult.Content.ReadAsStringAsync();
            ynabClient.Dispose();

            if (int.TryParse(ynabSyncResultContent, out var count) && count > 0)
            {
                _mailService.SendMail($"{count} items have been synced to ynab and need to be categorized.", $"{count} items synced to ynab");
            }

            return new OkObjectResult($"GoCardlessSync result: \t{goCardlessSyncResult}\nYnabSync result: \t\t{ynabSyncResultContent}");
        }
    }
}
