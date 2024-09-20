using GoCardlessToYnabSync.Options;
using GoCardlessToYnabSync.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoCardlessToYnabSync.Functions
{
    public class GoCardlessSync
    {
        private readonly ILogger<GoCardlessSync> _logger;
        private readonly FunctionUriOptions _functionUriOptions;
        private readonly GoCardlessSyncService _goCardlessSyncService;
        private readonly MailService _mailService;

        public GoCardlessSync(
            ILogger<GoCardlessSync> logger,
            IOptions<FunctionUriOptions> functionUriOptions,
            GoCardlessSyncService goCardlessSyncService, 
            MailService mailService)
        {
            _logger = logger;
            _functionUriOptions = functionUriOptions.Value;
            _goCardlessSyncService = goCardlessSyncService;
            _mailService = mailService;
        }

        [Function("GoCardlessSync")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {
            _logger.LogInformation("GoCardlessSync HTTP function triggered");

            string goCardlessResult;
            try
            {
                var goCardlessSyncResult = await _goCardlessSyncService.RetrieveFromGoCardless();
                goCardlessResult = $"GoCardlessSync result:\t{goCardlessSyncResult} items retrieved from GoCardless";
            }
            catch (Exception ex)
            {
                goCardlessResult = $"GoCardlessSync result:\t{ex.Message}";
            }

            var ynabClient = new HttpClient();
            var ynabClientResult = await ynabClient.GetAsync(_functionUriOptions.YnabSync);
            var ynabSyncResultContent = await ynabClientResult.Content.ReadAsStringAsync();
            ynabClient.Dispose();

            return new OkObjectResult($"{goCardlessResult}\n{ynabSyncResultContent}");
        }
    }
}
