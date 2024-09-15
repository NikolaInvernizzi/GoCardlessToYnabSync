using GoCardlessToYnabSync.Models;
using GoCardlessToYnabSync.Options;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Ynab.Api.Client;

namespace GoCardlessToYnabSync.Services
{
    public class YnabSyncService
    {
        private readonly IConfiguration _configuration;
        private readonly MailService _mailService;
        private readonly CosmosDbService _cosmosDbService;

        public YnabSyncService(IConfiguration configuration, MailService mailService, CosmosDbService cosmosDbService) {
            _configuration = configuration;
            _mailService = mailService;
            _cosmosDbService = cosmosDbService;
        }


        public async Task<string> PushTransactionsToYnab()
        {
            try
            {
                var transactions = await GetTransactions();
                if(transactions.Count == 0)
                {
                    return "Nothing to sync";
                }
                var result = await PushTransactionsToYnab(transactions);

                return result.ToString();
            }
            catch (Exception ex)
            {

                _mailService.SendMail(ex.Message, " FAIL - Error throw in PullTransactionsFromGoCardless");
                return ex.Message;
            }
        }

        public string GetPayee(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            var indexNarrative = text.IndexOf("narrative:");
            var narrative = text.Substring(indexNarrative);

            var indexArray = narrative.IndexOf("[");
            var stringArrayStr = narrative.Substring(indexArray);

            var stringArray = JsonConvert.DeserializeObject<List<string>>(stringArrayStr);

            if (stringArray is null || stringArray.Count < 3)
                return "";

            var firstPart = stringArray.FirstOrDefault()?.Trim();
            if (firstPart is null)
                return "";

            if (firstPart.Equals("EUROPESE DOMICILIERING VAN", StringComparison.InvariantCultureIgnoreCase))
            {
                if (stringArray.Count >= 8)
                    return $"{stringArray.ElementAt(2 - 1)} - {stringArray.ElementAt(8 - 1)}";

                if (stringArray.Count >= 2)
                    return stringArray.ElementAt(2 - 1);
            }
            else if (firstPart.Equals("MAANDELIJKSE BIJDRAGE", StringComparison.InvariantCultureIgnoreCase))
            {
                if (stringArray.Count >= 2)
                    return stringArray.ElementAt(2 - 1);
            }
            else if (firstPart.Equals("OVERSCHRIJVING IN EURO VAN REKENING", StringComparison.InvariantCultureIgnoreCase))
            {
                if (stringArray.Count >= 3)
                    return stringArray.ElementAt(3 - 1);
            }
            else if (firstPart.Equals("OVERSCHRIJVING IN EURO OP REKENING", StringComparison.InvariantCultureIgnoreCase))
            {
                if (stringArray.Count >= 4)
                    return stringArray.ElementAt(4 - 1);
            }
            else if (firstPart.Equals("MOBIELE BETALING", StringComparison.InvariantCultureIgnoreCase))
            {
                return "MOBIELE BETALING (P2P)";
            }
            else if (firstPart.Equals("STORTING VAN", StringComparison.InvariantCultureIgnoreCase))
            {
                if (stringArray.Count >= 2)
                    return stringArray.ElementAt(2 - 1);
            }
            else
            {
                if (stringArray.Count >= 3)
                    return stringArray.ElementAt(3 - 1);
            }

            return "";
        }

        public async Task<string> PushTransactionsToYnab(List<Transaction> transactions)
        {
            var ynabOptions = new YnabOptions();
            _configuration.GetSection(YnabOptions.Ynab).Bind(ynabOptions);

            var ynabClient = new YnabApiClient(new HttpClient()
            {
                DefaultRequestHeaders = {
                    Authorization = new AuthenticationHeaderValue("Bearer", ynabOptions.Secret)
                }
            });
            var accounts = ynabClient.GetAccountsAsync(ynabOptions.BudgetId, null).Result;
            var accountId = accounts.Data.Accounts.FirstOrDefault(a => a.Name.Equals(ynabOptions.AccountName, StringComparison.InvariantCultureIgnoreCase))?.Id;

            if (!accountId.HasValue || accountId.Value.ToString().Equals(new Guid().ToString()))
            {
                throw new Exception($"AccountId not found in ynab for BudgetId {ynabOptions.BudgetId} and accountname {ynabOptions.AccountName}");
            }

            var saveTransactions = BuildTransactions(transactions, accountId.Value);

            var createdTransResponse = await ynabClient.CreateTransactionAsync(ynabOptions.BudgetId, new PostTransactionsWrapper
            {
                Transactions = saveTransactions
            });

            if (createdTransResponse.Data?.Transactions is null)
                return "No transactions returned from ynab";

            var now = DateTime.Now;
            foreach (var item in createdTransResponse.Data.Transactions)
            {
                var t = transactions.SingleOrDefault(x => x.TransactionObject.AdditionalInformation is not null && x.TransactionObject.AdditionalInformation.Equals(item.Memo, StringComparison.InvariantCultureIgnoreCase));
                if (t is not null)
                {
                    t.SyncedOn = now;
                }
            }

            await _cosmosDbService.AddOrUpdateTransactions(transactions.Where(x => x.SyncedOn.HasValue).ToList());

            return transactions.Count.ToString();
        }

        private List<SaveTransaction> BuildTransactions(List<Transaction> transactions, Guid accountId)
        {
            var newTransactions = new List<SaveTransaction>();
            foreach (var t in transactions)
            {
                var tObj = t.TransactionObject;

                decimal amount = tObj.TransactionAmount.Amount;
                var payee = GetPayee(tObj.AdditionalInformation);

                newTransactions.Add(new SaveTransaction
                {
                    Account_id = accountId,
                    Amount = Convert.ToInt64(amount * 1000), // milliunits
                    Date = t.BookingDate,
                    Payee_name = payee,
                    Memo = tObj.AdditionalInformation
                });
            }
            return newTransactions;
        }

        public async Task<List<Transaction>> GetTransactions()
        {
            var transactions = await _cosmosDbService.GetTransactionsNoSynced();
            return transactions;
        }
    }
}
