using GoCardlessToYnabSync.Options;
using GoCardlessToYnabSync.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RobinTTY.NordigenApiClient.Models.Requests;

namespace GoCardlessToYnabSync.Functions
{
    public class GoCardlessRetrieveInstitutions
    {
        private readonly ILogger<GoCardlessSync> _logger;
        private readonly GoCardlessSyncService _goCardlessSyncService;

        public GoCardlessRetrieveInstitutions(ILogger<GoCardlessSync> logger, GoCardlessSyncService goCardlessSyncService)
        {
            _logger = logger;
            _goCardlessSyncService = goCardlessSyncService;
        }

        [Function("GoCardlessRetrieveInstitutions")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "get")] HttpRequest req)
        {
            _logger.LogInformation("GoCardless Retrieve Institution ids HTTP function triggered");

            string goCardlessInstitutionResults;
            try
            {
                var country = req.Query["country"].FirstOrDefault();
                if(country is not null)
                {
                    country = char.ToUpper(country[0]) + country.Substring(1).ToLower();
                }
                if (Enum.TryParse(country, out SupportedCountry enumCountry))
                {
                    goCardlessInstitutionResults = await _goCardlessSyncService.GetInstitutionIds(enumCountry);
                }
                else
                {
                    goCardlessInstitutionResults = $"Countrycode `{country}` is not supported.";
                    goCardlessInstitutionResults += $"\nSupportedCountries:\n";
                    foreach (SupportedCountry supportedCountry in Enum.GetValues(typeof(SupportedCountry)))
                    {
                        goCardlessInstitutionResults += $"\n - {supportedCountry}";
                    }
                }
            }
            catch (Exception ex)
            {
                goCardlessInstitutionResults = $"Retrieving GoCardless Institution Ids result:\t{ex.Message}";
            }       

            return new OkObjectResult($"{goCardlessInstitutionResults}");
        }
    }
}
