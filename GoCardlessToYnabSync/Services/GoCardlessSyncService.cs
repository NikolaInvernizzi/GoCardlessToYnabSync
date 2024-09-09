using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using GoCardlessToYnabSync.Models;
using RobinTTY.NordigenApiClient.Models;
using RobinTTY.NordigenApiClient;
using GoCardlessToYnabSync.Options;

namespace GoCardlessToYnabSync.Services
{
    public class GoCardlessSyncService
    {
        private readonly IConfiguration _configuration;
        private readonly CosmosDbService _cosmosDbService;

        public GoCardlessSyncService(IConfiguration configuration, CosmosDbService cosmosDbService) 
        {
            _configuration = configuration;
            _cosmosDbService = cosmosDbService;
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
                        SendAuthMail(newReq.AuthLink, goCardlessOptions.BankId);
                        return "sent authmail";
                    }
                    catch (Exception ex)
                    {
                        SendFailedNewRequisitionMail(ex.Message, "Failed to retrieve new requisition id from gocarddless");
                        return ex.Message;
                    }
                }
                else
                {

                    var resultStatus = await GetStatusFromRequisition(requisition);
                    if (resultStatus.Status is null || resultStatus.Requisition is null)
                    {
                        SendFailedNewRequisitionMail("could not retrieve req id", "Failed to retrieve requisition id from gocarddless");
                        return "cant get status  or requisition id from gocardless";
                    }
                    else if (resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.Expired)
                    {
                        requisition.Valid = false;
                        await _cosmosDbService.UpdateRequistion(requisition);
                        SendFailedNewRequisitionMail("req id expired", "Retrieved requisition id from gocarddless is expired");
                        return await PullTransactionsFromGoCardless();
                    }
                    else if (resultStatus.Status == RobinTTY.NordigenApiClient.Models.Responses.RequisitionStatus.UndergoingAuthentication)
                    {
                        SendAuthMail(resultStatus.Requisition?.AuthenticationLink?.ToString() ?? "issue with authlink", goCardlessOptions.BankId);
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
                        SendFailedNewRequisitionMail(resultStatus.Status?.ToString() ?? "STATUS FAILED", "Failed to retrieve requisition id from gocarddless");
                        requisition.Valid = false;
                        await _cosmosDbService.UpdateRequistion(requisition);
                        return await PullTransactionsFromGoCardless();
                    }
                }
            }
            catch (Exception ex){
                return ex.Message;
            }
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
                var groupedByEntryReference = transactionsResponse.Result.BookedTransactions.ToList().GroupBy(x => x.EntryReference);
                
                var transactions = new List<Transaction>();
                var errors = new List<string>();
                foreach (var item in groupedByEntryReference)
                {
                    try
                    {
                        if (item.Key is null)
                        {
                            throw new Exception($"key is null");
                        }

                        if (item.All(x => !x.BookingDate.HasValue))
                        {
                            throw new Exception($"{item.Key}: bookingdate has no value");
                        }
                        if (item.All(x => string.IsNullOrWhiteSpace(x.AdditionalInformation)))
                        {
                            throw new Exception($"{item.Key}: additional info is empty");
                        }

                        transactions.Add(new Transaction
                        {
                            Id = Guid.NewGuid().ToString(),
                            EntryReference = item.Key,
                            BookingDate = item.First().BookingDate!.Value,
                            TransactionObject = item.First(x => x.AdditionalInformation!.Contains("statementReference")),
                            SyncedOn = null
                        });

                    }catch(Exception ex)
                    {
                        errors.Add($"{ex.Message}");
                    }
                }
                if(errors.Count > 0)
                {
                    SendFailedNewRequisitionMail(string.Join(", ", errors), "errors occured when creating transactions");
                    return $"error occured when creating transaction to add to db: {string.Join(',', errors)}";
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

        private void SendAuthMail(string authLink, string bankId)
        {
            var smptOptions = new SmptOptions();
            _configuration.GetSection(SmptOptions.Smpt).Bind(smptOptions);

            MailMessage mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(smptOptions.Email);
            mailMessage.To.Add(smptOptions.SendTo);
            mailMessage.Subject = $"New requisition Id to authenticate for bank {bankId}";
            mailMessage.Body = $"Hello {smptOptions.Email}, \n\n You're old requisition Id was invalid, use the link below to authenticate the new one:\n {authLink}";


            SmtpClient smtpClient = new SmtpClient();
            smtpClient.Host = smptOptions.Host;
            smtpClient.Port = smptOptions.Port;
            smtpClient.UseDefaultCredentials = false;
            smtpClient.Credentials = new NetworkCredential(smptOptions.Email, smptOptions.Password);
            smtpClient.EnableSsl = true;

            smtpClient.Send(mailMessage);
        }
      
        private void SendFailedNewRequisitionMail(string exceptionMessage, string message)
        {
            var smptOptions = new SmptOptions();
            _configuration.GetSection(SmptOptions.Smpt).Bind(smptOptions);

            MailMessage mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(smptOptions.Email);
            mailMessage.To.Add(smptOptions.SendTo);
            mailMessage.Subject = $"Error occured in GoCardless to Ynab sync";
            mailMessage.Body = $"Hello {smptOptions.Email}, \n\n {message}: {exceptionMessage}";


            SmtpClient smtpClient = new SmtpClient();
            smtpClient.Host = smptOptions.Host;
            smtpClient.Port = smptOptions.Port;
            smtpClient.UseDefaultCredentials = false;
            smtpClient.Credentials = new NetworkCredential(smptOptions.Email, smptOptions.Password);
            smtpClient.EnableSsl = true;

            smtpClient.Send(mailMessage);
        }
    }
}
