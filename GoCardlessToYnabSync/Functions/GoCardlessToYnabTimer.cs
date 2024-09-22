using System;
using GoCardlessToYnabSync.Options;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GoCardlessToYnabSync.Functions
{
    public class GoCardlessToYnabTimer
    {
        private readonly ILogger _logger;
        private readonly FunctionUriOptions _functionUriOptions;

        public GoCardlessToYnabTimer(
            ILoggerFactory loggerFactory, 
            IOptions<FunctionUriOptions> functionUriOptions)
        {
            _logger = loggerFactory.CreateLogger<GoCardlessToYnabTimer>();
            _functionUriOptions = functionUriOptions.Value;
        }

        // https://crontab.guru/
        // https://docs.microsoft.com/en-us/azure/azure-functions/functions-bindings-timer#cron-expressions
        // 0 0 */4 * * * = once every 4 hours

        [Function("GoCardlessToYnabTimer")]
        public async Task Run([TimerTrigger("%TimerTriggerCronExpression%")] TimerInfo myTimer)
        {
            _logger.LogInformation($"Starting up GoCardLessSync with timer function: {DateTime.Now}");

            var client = new HttpClient();
            await client.GetAsync(_functionUriOptions.GoCardlessSync);
            client.Dispose();

            if (myTimer.ScheduleStatus is not null)
            {
                _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");
            }
        }
    }
}
