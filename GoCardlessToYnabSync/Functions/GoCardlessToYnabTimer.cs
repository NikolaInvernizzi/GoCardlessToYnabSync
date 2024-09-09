using System;
using GoCardlessToYnabSync.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GoCardlessToYnabSync.Functions
{
    public class GoCardlessToYnabTimer
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        public GoCardlessToYnabTimer(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<GoCardlessToYnabTimer>();
            _configuration = configuration;
        }

        // https://crontab.guru/
        // https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-timer#cron-expressions
        // 0 0 */4 * * * = once every 4 hours

        [Function("GoCardlessToYnabTimer")]
        public async Task Run([TimerTrigger("0 0 */4 * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation($"Starting up GoCardLessSync with timer function: {DateTime.Now}");

            var functionUris = new FunctionUriOptions();
            _configuration.GetSection(FunctionUriOptions.FunctionUris).Bind(functionUris);

            var client = new HttpClient();
            await client.GetAsync(functionUris.GoCardlessSync);
            client.Dispose();

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            }
        }
    }
}
