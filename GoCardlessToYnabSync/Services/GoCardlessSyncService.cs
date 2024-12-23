﻿using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using GoCardlessToYnabSync.Models;
using RobinTTY.NordigenApiClient.Models;
using RobinTTY.NordigenApiClient;
using GoCardlessToYnabSync.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using System.Net.Http;
using RobinTTY.NordigenApiClient.Models.Requests;

namespace GoCardlessToYnabSync.Services
{
    public class GoCardlessSyncService
    {
        private readonly CosmosDbService _cosmosDbService;
        private readonly MailService _mailService;
        private readonly GoCardlessOptions _goCardlessOptions;
        private readonly FunctionUriOptions _functionUriOptions;
        private readonly NordigenClient _nordigenClient;

        public GoCardlessSyncService(
            CosmosDbService cosmosDbService, 
            MailService mailService,
            IOptions<FunctionUriOptions> functionUriOptions,
            IOptions<GoCardlessOptions> goCardlessOptions) 
        {
            _cosmosDbService = cosmosDbService;
            _mailService = mailService;
            _functionUriOptions = functionUriOptions.Value;
            _goCardlessOptions = goCardlessOptions.Value;

            HttpClient httpClient = new();
            NordigenClientCredentials credentials = new(_goCardlessOptions.SecretId, _goCardlessOptions.Secret);
            _nordigenClient = new NordigenClient(httpClient, credentials);
        }


        public async Task<string> GetInstitutionIds(SupportedCountry countryCode)
        {
            var result = "Institution Name                                          |ID\n----------------------------------------------------------|-------------------------------";
            var institutionsResponse = await _nordigenClient.InstitutionsEndpoint.GetInstitutions("sLovAkiA");
            if (institutionsResponse.IsSuccess)
                institutionsResponse.Result.ForEach(institution =>
                {
                    result += $"\n{institution.Name.PadRight(58)}|{institution.Id}";
                });
            else
                result += $"\nCouldn't retrieve institutions, error: {institutionsResponse.Error.Summary}";


            return result;
        }


        public async Task<int> RetrieveFromGoCardless()
        {
            var requisition = await GetLastRequisitionId();

            if (requisition is null)
            {
                await CreateNewRequisitionId();
                throw new Exception("Authentication (1) mail sent");
            }
            else
            {
                return await ExecuteActionBasedOnStatus(requisition);
            }
        }

        private async Task<int> ExecuteActionBasedOnStatus(Requisition requisition)
        {
            var resultStatus = await GetStatusFromRequisition(requisition);

            if (resultStatus.Status is null || resultStatus.Requisition is null)
            {
                _mailService.SendMail("Could not retrieve requisition id or status", "Failed to retrieve requisition id or status from GoCardless");
                throw new Exception("cant get status  or requisition id from gocardless");
            }
            else if (resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.Expired
                || resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.Suspended
                || resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.Rejected)
            {
                requisition.Valid = false;
                await _cosmosDbService.UpdateRequistion(requisition);

                // double check
                var lastReq = await GetLastRequisitionId();
                if (lastReq is null)
                {
                    await CreateNewRequisitionId();
                    throw new Exception($"Requistion id was expired, new requistion id requested, authentication link should be sent");
                }
                else
                {
                    return await RetrieveFromGoCardless();
                }
            }
            else if (resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.UndergoingAuthentication
                || resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.Created
                || resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.GivingConsent
                || resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.SelectingAccounts
                || resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.GrantingAccess)
            {
                _mailService.SendAuthMail(resultStatus.Requisition?.AuthenticationLink?.ToString() ?? "issue with authlink!!", true);
                throw new Exception("Resent authentication mail, last requisition id was not authenticated with bank yet!");
            }
            else if (resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.Linked)
            {
                if (!requisition.Valid.HasValue)
                {
                    requisition.Valid = true;
                    requisition = await _cosmosDbService.UpdateRequistion(requisition);
                }

                var accountId = await GetAccountId(requisition);

                if (accountId is null)
                    throw new Exception("No bank linked to requisition id");

                var result = await SyncTransaction(accountId);

                requisition.LastSyncOn = DateTime.UtcNow;
                requisition = await _cosmosDbService.UpdateRequistion(requisition);
                
                return result;
            }
            else
            {
                var msg = $"Unknown status: {resultStatus.Status.ToString()}, expand code to handle this status.";
                _mailService.SendMail($"Unknown status: {resultStatus.Status.ToString()}", msg);
                //requisition.Valid = false;
                //await _cosmosDbService.UpdateRequistion(requisition);
                //return await RetrieveFromGoCardless();
                throw new Exception(msg);
            }
        }

        private async Task CreateNewRequisitionId()
        {
            var newReq = await GetNewRequisitionId();

            if (newReq.RequisitionId is null || newReq.AuthLink is null)
            {
                throw new Exception($"RequisitionId or AuthLink is null:  '{newReq.RequisitionId}' or '{newReq.AuthLink}'");
            }

            var newRequisition = new Requisition
            {
                RequisitionId = newReq.RequisitionId,
                Valid = null,
                LastSyncOn = null,
                CreatedOn = DateTime.UtcNow,
            };
            await _cosmosDbService.AddNewRequisition(newRequisition);

            _mailService.SendAuthMail(newReq.AuthLink);
        }

        private async Task<int> SyncTransaction(string accountId)
        {
            var transactionSince = DateTime.UtcNow.AddDays(_goCardlessOptions.DaysInPastToRetrieve * -1);
            var transactionsResponse = await _nordigenClient.AccountsEndpoint.GetTransactions(accountId, 
                                                            DateOnly.FromDateTime(transactionSince));
            if (transactionsResponse.IsSuccess)
            {
                if (transactionsResponse.Result.BookedTransactions.Count() == 0)
                {
                    throw new Exception("No transaction retrieved from GoCardless");
                }

                List<IGrouping<string, RobinTTY.NordigenApiClient.Models.Responses.Transaction>> groupedByEntryReference = transactionsResponse.Result.BookedTransactions
                                                                        .Where(x => !string.IsNullOrWhiteSpace(x.EntryReference))
                                                                        .GroupBy(x => x.EntryReference ?? "")
                                                                        .ToList();
                if(groupedByEntryReference is null)
                {
                    throw new Exception("Grouped GoCardless transactions list is null, how??");
                }

                var transactions = new List<Transaction>();
                var errors = new List<string>();
                foreach (var item in groupedByEntryReference.Where(x => x.Key is not null).ToList())
                {
                    try
                    {
                        if (item is null)
                        {
                            throw new Exception("Item in Grouped GoCardless transactions was null, how??");
                        }
                        if (item.Key is null)
                        {
                            errors.Add($"Key is null, how again??");
                        }
                        if (item.Count() == 0)
                        {
                            errors.Add($"{item.Key}: has no items");
                        }

                        if (item.Any(x => !x.BookingDate.HasValue))
                        {
                            errors.Add($"{item.Key}: bookingdate (1) has no value");
                        }
                        var bookingdate = item.FirstOrDefault()?.BookingDate;
                        if (!bookingdate.HasValue)
                        {
                            errors.Add($"{item.Key}: bookingdate (2) has no value");
                        }

                        if (item.Any(x => string.IsNullOrWhiteSpace(x.AdditionalInformation)))
                        {
                            errors.Add($"{item.Key}: additional info (1) is empty");
                        }
                        var additionalInformation = item.FirstOrDefault(x => x.AdditionalInformation!.Contains("narrative:"));
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
                        errors.Add($"Exception: {ex.Message}");
                    }
                }

                if(errors.Count > 0)
                {
                    _mailService.SendMail(string.Join(", ", errors), "errors occured when creating transactions");
                    throw new Exception($"Error occured when creating transaction to add to db: {string.Join('\n', errors)}");
                }

                List<Transaction> existingTransactions = await _cosmosDbService.GetTransactionsSince(transactionSince.AddDays(-7));
                var existingEntryReferences = existingTransactions.Select(t => t.EntryReference).ToList();
                transactions = transactions.Where(t => !existingEntryReferences.Contains(t.EntryReference)).ToList();

                await _cosmosDbService.AddOrUpdateTransactions(transactions);
                return transactions.Count;
            }
            throw new Exception($"Failed to retrieve transaction from GoCardless: {transactionsResponse.Error.Detail}");
        }

        private async Task<string?> GetAccountId(Requisition requisition)
        {
            var accountsResponse = await _nordigenClient.RequisitionsEndpoint.GetRequisition(requisition.RequisitionId);
            if (accountsResponse.IsSuccess)
            {
                return accountsResponse.Result.Accounts.First().ToString();
            }
            throw new Exception("Failed to retrieve accounts");
        }

        private async Task<(RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus? Status, RobinTTY.NordigenApiClient.Models.Responses.Requisition? Requisition)> GetStatusFromRequisition(Requisition requisition)
        {
            try
            {
                var reqResult = await _nordigenClient.RequisitionsEndpoint.GetRequisition(requisition.RequisitionId);

                return (reqResult.Result?.Status, reqResult.Result);
            }
            catch (Exception ex)
            {
                _mailService.SendMail(ex.Message, "Failed to retrieve status for requisition id from GoCardless");
                throw;
            }
        }


        private async Task<(string? RequisitionId, string? AuthLink)> GetNewRequisitionId()
        {
            try {
                var requisitionResponse = await _nordigenClient.RequisitionsEndpoint.CreateRequisition(_goCardlessOptions.BankId, new Uri(_functionUriOptions.GoCardlessSync));

                if (requisitionResponse.IsSuccess)
                {
                    return (requisitionResponse.Result.Id.ToString(), requisitionResponse.Result.AuthenticationLink.ToString());
                }

                return (null, null);
            }
            catch (Exception ex)
            {
                _mailService.SendMail(ex.Message, "Failed to retrieve new requisition id from GoCardless");
                throw;
            }
        }

        private async Task<Requisition?> GetLastRequisitionId()
        {
            var lastRequisition = await _cosmosDbService.GetLastRequisitionId();
            return lastRequisition;
        }


        public async Task<string> PurgeGoCardlessRequisitionIds()
        {
            var allRequistionsIdsGoCardlessResponse = await _nordigenClient.RequisitionsEndpoint.GetRequisitions(100, 0);

            if (!allRequistionsIdsGoCardlessResponse.IsSuccess)
            {
                throw new Exception("Failed to retrieve requisition ids from GoCardless");
            }

            var allRequistionsIdsDB = await _cosmosDbService.GetAllRequistionIds();

            var allRequistionIdsDbNotValid = allRequistionsIdsDB.Where(r => !r.Valid.HasValue || r.Valid.Value).Select(r => r.RequisitionId);

            var requisitionIdsToDelete = allRequistionsIdsGoCardlessResponse.Result.Results.Where(r => !allRequistionIdsDbNotValid.Contains(r.Id.ToString()));

            var count = 0;
            var failedPurges = new List<string>();
            foreach (var item in requisitionIdsToDelete)
            {
                var deleteRequistionIdResponse = await _nordigenClient.RequisitionsEndpoint.DeleteRequisition(item.Id);
                if (deleteRequistionIdResponse.IsSuccess)
                {
                    count++;
                    var result = deleteRequistionIdResponse.Result;
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(deleteRequistionIdResponse.Result?.Detail))
                        failedPurges.Add(deleteRequistionIdResponse.Result.Detail);
                    else
                        failedPurges.Add($"Failed to purge {item.Id}");
                }
            }
            if (failedPurges.Count > 0)
            {
                return $"\n\tSuccesfully purged {count} requistion Ids from GoCardless\n\tBut failed to purge the following: {string.Join("\n\t\t- ", failedPurges)}";
            }

            return $"Succesfully purged {count} requistion Ids from GoCardless";
        }
    }
}
