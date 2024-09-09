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

        public GoCardlessSync(ILogger<GoCardlessSync> logger, IConfiguration configuration, GoCardlessSyncService goCardlessSyncService)
        {
            _logger = logger;
            _configuration = configuration;
            _goCardlessSyncService = goCardlessSyncService;
        }

        [Function("GoCardlessSync")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {
            _logger.LogInformation("GoCardlessSync HTTP function triggered");

            var result = await _goCardlessSyncService.PullTransactionsFromGoCardless();

            var functionUris = new FunctionUriOptions();
            _configuration.GetSection(FunctionUriOptions.FunctionUris).Bind(functionUris);

            var client = new HttpClient();
            var clientResult = await client.GetAsync(functionUris.YnabSync);
            var content = await clientResult.Content.ReadAsStringAsync();
            client.Dispose();

            return new OkObjectResult($"GoCardlessSync result: {result}\nYnabSync result: {content}");
        }
    }
}
