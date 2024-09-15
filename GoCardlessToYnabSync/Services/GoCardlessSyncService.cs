using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using GoCardlessToYnabSync.Models;
using RobinTTY.NordigenApiClient.Models;
using RobinTTY.NordigenApiClient;
using GoCardlessToYnabSync.Options;
using Microsoft.IdentityModel.Tokens;

namespace GoCardlessToYnabSync.Services
{
    public class GoCardlessSyncService
    {
        private readonly IConfiguration _configuration;
        private readonly CosmosDbService _cosmosDbService;
        private readonly MailService _mailService;

        public GoCardlessSyncService(IConfiguration configuration, CosmosDbService cosmosDbService, MailService mailService) 
        {
            _configuration = configuration;
            _cosmosDbService = cosmosDbService;
            _mailService = mailService;
        }

        public async Task<string> PullTransactionsFromGoCardless()
        {
            try
            {
                var goCardlessOptions = new GoCardlessOptions();
                _configuration.GetSection(GoCardlessOptions.GoCardless).Bind(goCardlessOptions);

                var requisition = await GetLastRequisitionId();
                
                if (requisition is null)
                {
                    try
                    {
                        var newReq = await GetNewRequisitionId();

                        return await CreateNewRequisitionId(goCardlessOptions, newReq);
                    }
                    catch (Exception ex)
                    {
                        _mailService.SendMail(ex.Message, "Failed to retrieve new requisition id from GoCardless");
                        return ex.Message;
                    }
                }
                else
                {

                    var resultStatus = await GetStatusFromRequisition(requisition);
                    return await ExecuteActionBasedOnStatus(goCardlessOptions, requisition, resultStatus);
                }
            }
            catch (Exception ex)
            {
                _mailService.SendMail(ex.Message, "GoCardlessToYnabSync Failed: Error throw in PullTransactionsFromGoCardless");
                return ex.Message;
            }
        }

        private async Task<string> ExecuteActionBasedOnStatus(GoCardlessOptions goCardlessOptions, Requisition requisition, (RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus? Status, RobinTTY.NordigenApiClient.Models.Responses.Requisition? Requisition) resultStatus)
        {
            if (resultStatus.Status is null || resultStatus.Requisition is null)
            {
                _mailService.SendMail("could not retrieve req id", "Failed to retrieve requisition id from gocarddless");
                return "cant get status  or requisition id from gocardless";
            }
            else if (resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.Expired)
            {
                requisition.Valid = false;
                await _cosmosDbService.UpdateRequistion(requisition);
                _mailService.SendMail("req id expired", "Retrieved requisition id from gocarddless is expired");
                return await PullTransactionsFromGoCardless();
            }
            else if (resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.UndergoingAuthentication)
            {
                _mailService.SendAuthMail(resultStatus.Requisition?.AuthenticationLink?.ToString() ?? "issue with authlink!!", goCardlessOptions.BankId);
                return "resend authmail, last requisition id was not authenticated with bank yet";
            }
            else if (resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.Linked)
            {
                requisition.Valid = true;
                requisition = await _cosmosDbService.UpdateRequistion(requisition);

                var accountId = await GetAccountId(requisition);

                if (accountId is null)
                    return "No bank linked to requisition id";

                return await SyncTransaction(accountId);
            }
            else
            {
                _mailService.SendMail(resultStatus.Status?.ToString() ?? "STATUS FAILED", "Failed to retrieve requisition id from GoCardless");
                requisition.Valid = false;
                await _cosmosDbService.UpdateRequistion(requisition);
                return await PullTransactionsFromGoCardless();
            }
        }

        private async Task<string> CreateNewRequisitionId(GoCardlessOptions goCardlessOptions, (string? RequisitionId, string? AuthLink) newReq)
        {
            if (newReq.RequisitionId is null || newReq.AuthLink is null)
            {
                throw new Exception($"RequisitionId or AuthLink is null:  '{newReq.RequisitionId}' or '{newReq.AuthLink}'");
            }

            var newRequisition = new Requisition
            {
                RequisitionId = newReq.RequisitionId,
                Valid = false,
                LastSyncOn = null,
                CreatedOn = DateTime.Now,
            };
            await _cosmosDbService.AddNewRequisition(newRequisition);
            _mailService.SendAuthMail(newReq.AuthLink, goCardlessOptions.BankId);
            return "authmail sent";
        }

        private async Task<string> SyncTransaction(string accountId)
        {
            var goCardlessOptions = new GoCardlessOptions();
            _configuration.GetSection(GoCardlessOptions.GoCardless).Bind(goCardlessOptions);

            var functionUris = new FunctionUriOptions();
            _configuration.GetSection(FunctionUriOptions.FunctionUris).Bind(functionUris);

            using var httpClient = new HttpClient();
            var credentials = new NordigenClientCredentials(goCardlessOptions.SecretId, goCardlessOptions.Secret);
            var client = new NordigenClient(httpClient, credentials);

            var transactionSince = DateTime.UtcNow.AddDays(-goCardlessOptions.DaysInPastToRetrieve);
            var transactionsResponse = await client.AccountsEndpoint.GetTransactions(accountId, 
                                                            DateOnly.FromDateTime(transactionSince));
            if (transactionsResponse.IsSuccess)
            {
                if (transactionsResponse.Result.BookedTransactions.Count() == 0)
                {
                    return "No transaction retrieved from gocardless";
                }

                List<IGrouping<string, RobinTTY.NordigenApiClient.Models.Responses.Transaction>> groupedByEntryReference = transactionsResponse.Result.BookedTransactions
                                                                        .Where(x => !string.IsNullOrWhiteSpace(x.EntryReference))
                                                                        .GroupBy(x => x.EntryReference ?? "")
                                                                        .ToList();
                if(groupedByEntryReference is null)
                {
                    return "Grouped gocardless transactions list is null, how??";
                }

                var transactions = new List<Transaction>();
                var errors = new List<string>();
                foreach (var item in groupedByEntryReference.Where(x => x.Key is not null).ToList())
                {
                    try
                    {
                        if (item is null)
                        {
                            return "item in Grouped gocardless transactions was null";
                        }
                        if (item.Key is null)
                        {
                            errors.Add($"key is null");
                        }
                        if (item.Count() == 0)
                        {
                            errors.Add($"{item.Key}: has no items");
                        }

                        if (item.Any(x => !x.BookingDate.HasValue))
                        {
                            errors.Add($"{item.Key}: bookingdate1 has no value");
                        }
                        var bookingdate = item.FirstOrDefault()?.BookingDate;
                        if (!bookingdate.HasValue)
                        {
                            errors.Add($"{item.Key}: bookingdate2 has no value");
                        }

                        if (item.Any(x => string.IsNullOrWhiteSpace(x.AdditionalInformation)))
                        {
                            errors.Add($"{item.Key}: additional info1 is empty");
                        }
                        var additionalInformation = item.FirstOrDefault(x => x.AdditionalInformation!.Contains("statementReference"));
                        if (additionalInformation is null || string.IsNullOrWhiteSpace(additionalInformation.AdditionalInformation))
                        {
                            // if no statementreference is available its because the transaction is not complete yet,
                            // wait with adding it to transaction till there is a statementreference
                            continue;
                        }

                        if (errors.Count == 0 && item?.Key is not null)
                        {
                            if (bookingdate.HasValue && additionalInformation is not null)
                            {
                                transactions.Add(new Transaction
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    EntryReference = item.Key,
                                    BookingDate = bookingdate.Value,
                                    TransactionObject = additionalInformation,
                                    SyncedOn = null
                                });
                            }
                            else
                            {
                                errors.Add($"{item.Key}: booking date is null or additional information is null");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($" exception: {ex.Message}");
                    }
                }

                if(errors.Count > 0)
                {
                    _mailService.SendMail(string.Join(", ", errors), "errors occured when creating transactions");
                    return $"error occured when creating transaction to add to db: {string.Join('\n', errors)}";
                }

                List<Transaction> existingTransactions = await _cosmosDbService.GetTransactionsSince(transactionSince.AddDays(-7));
                var existingEntryReferences = existingTransactions.Select(t => t.EntryReference).ToList();
                transactions = transactions.Where(t => !existingEntryReferences.Contains(t.EntryReference)).ToList();

                await _cosmosDbService.AddOrUpdateTransactions(transactions);
                return transactions.Count.ToString();
            }
            return $"failed to retrieve transaction from gocardless: {transactionsResponse.Error.Detail}";
        }

        private async Task<string?> GetAccountId(Requisition requisition)
        {
            var goCardlessOptions = new GoCardlessOptions();
            _configuration.GetSection(GoCardlessOptions.GoCardless).Bind(goCardlessOptions);

            var functionUris = new FunctionUriOptions();
            _configuration.GetSection(FunctionUriOptions.FunctionUris).Bind(functionUris);

            using var httpClient = new HttpClient();
            var credentials = new NordigenClientCredentials(goCardlessOptions.SecretId, goCardlessOptions.Secret);
            var client = new NordigenClient(httpClient, credentials);

            var accountsResponse = await client.RequisitionsEndpoint.GetRequisition(requisition.RequisitionId);
            if (accountsResponse.IsSuccess)
            {
                return accountsResponse.Result.Accounts.First().ToString();
            }
            return null;
        }

        private async Task<(RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus? Status, RobinTTY.NordigenApiClient.Models.Responses.Requisition? Requisition)> GetStatusFromRequisition(Requisition requisition)
        {
            var goCardlessOptions = new GoCardlessOptions();
            _configuration.GetSection(GoCardlessOptions.GoCardless).Bind(goCardlessOptions);

            var functionUris = new FunctionUriOptions();
            _configuration.GetSection(FunctionUriOptions.FunctionUris).Bind(functionUris);

            using var httpClient = new HttpClient();
            var credentials = new NordigenClientCredentials(goCardlessOptions.SecretId, goCardlessOptions.Secret);
            var client = new NordigenClient(httpClient, credentials);

            var reqResult = await client.RequisitionsEndpoint.GetRequisition(requisition.RequisitionId);

            return (reqResult.Result?.Status, reqResult.Result);
        }


        private async Task<(string? RequisitionId, string? AuthLink)> GetNewRequisitionId()
        {
            var goCardlessOptions = new GoCardlessOptions();
            _configuration.GetSection(GoCardlessOptions.GoCardless).Bind(goCardlessOptions);

            var functionUris = new FunctionUriOptions();
            _configuration.GetSection(FunctionUriOptions.FunctionUris).Bind(functionUris);

            using var httpClient = new HttpClient();
            var credentials = new NordigenClientCredentials(goCardlessOptions.SecretId, goCardlessOptions.Secret);
            var client = new NordigenClient(httpClient, credentials);

            var requisitionResponse = await client.RequisitionsEndpoint.CreateRequisition(goCardlessOptions.BankId, new Uri(functionUris.GoCardlessSync));

            if (requisitionResponse.IsSuccess)
            {
                return (requisitionResponse.Result.Id.ToString(), requisitionResponse.Result.AuthenticationLink.ToString());
            }

            return (null, null);
        }


        private async Task<Requisition?> GetLastRequisitionId()
        {
            var lastRequisition = await _cosmosDbService.GetLastRequisitionId();
            return lastRequisition;
        }

    }
}
