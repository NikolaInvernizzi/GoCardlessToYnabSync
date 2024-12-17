using GoCardlessToYnabSync.Models;
using GoCardlessToYnabSync.Options;
using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Ynab.Api.Client;

namespace GoCardlessToYnabSync.Services
{
    public class YnabSyncService
    {
        private readonly MailService _mailService;
        private readonly CosmosDbService _cosmosDbService;
        private readonly YnabOptions _ynabOptions;
        private readonly YnabApiClient _ynabApiClient;

        public YnabSyncService(
            MailService mailService, 
            CosmosDbService cosmosDbService,
            IOptions<YnabOptions> ynabOptions
            )
        {
            _mailService = mailService;
            _cosmosDbService = cosmosDbService;
            _ynabOptions = ynabOptions.Value;

            _ynabApiClient = new YnabApiClient(new HttpClient()
            {
                DefaultRequestHeaders = {
                    Authorization = new AuthenticationHeaderValue("Bearer", _ynabOptions.Secret)
                }
            });
        }

        public async Task<int> SyncToYnab()
        {
            var transactions = await GetTransactions();
            if(transactions.Count == 0)
            {
                throw new Exception("Nothing to sync");
            }

            var result = await PushTransactionsToYnab(transactions);
            _mailService.SendMail($"{result} items have been synced to Ynab and need to be categorized.", $"{result} items synced to Ynab");
                
            return result;            
        }

        public async Task<int> PushTransactionsToYnab(List<Transaction> transactions)
        {
            var accounts = _ynabApiClient.GetAccountsAsync(_ynabOptions.BudgetId, null).Result;
            var accountId = accounts.Data.Accounts.FirstOrDefault(a => a.Name.Equals(_ynabOptions.AccountName, StringComparison.InvariantCultureIgnoreCase))?.Id;
            if (!accountId.HasValue || accountId.Value.ToString().Equals(new Guid().ToString()))
            {
                _mailService.SendMail($"AccountId not found in Ynab for BudgetId {_ynabOptions.BudgetId} and accountname {_ynabOptions.AccountName}", $"Ynab AccountId not found for Given Budget");
                throw new Exception($"AccountId not found in Ynab for BudgetId {_ynabOptions.BudgetId} and accountname {_ynabOptions.AccountName}");
            }

            var saveTransactions = BuildTransactions(transactions, accountId.Value);
            if (saveTransactions.Count == 0)
                throw new Exception("Nothing to sync to Ynab");

            var createdTransResponse = await _ynabApiClient.CreateTransactionAsync(_ynabOptions.BudgetId, new PostTransactionsWrapper
            {
                Transactions = saveTransactions
            });

            if (createdTransResponse.Data?.Transactions is null)
            {
                _mailService.SendMail($"No transactions returned from Ynab.\n You may want to look into this!", $"No transactions returned from Ynab, hmmm?");
                throw new Exception("No transactions returned from Ynab, hmmm?");
            }

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

            return transactions.Count;
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

        public string GetPayee(string? text)
        {
            // Return if string is empty
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            // Find the index of the "narrative:" keyword in the text
            int indexNarrative = text.IndexOf("narrative:");
            if (indexNarrative == -1)
                return string.Empty;

            // Extract the narrative part of the string
            string narrative = text.Substring(indexNarrative);

            // Find the index of the starting bracket "[" within the narrative
            int indexArray = narrative.IndexOf("[");
            if (indexArray == -1)
                return string.Empty;

            // Extract the JSON array as a string
            string stringArrayStr = narrative.Substring(indexArray);

            // Deserialize the string to a List<string>
            var stringArray = JsonConvert.DeserializeObject<List<string>>(stringArrayStr);

            // Return empty if the array is null or contains less than 3 items
            if (stringArray == null || stringArray.Count < 3)
                return string.Empty;

            // Get the first item in the array, trim any leading/trailing spaces
            string? firstPart = stringArray.FirstOrDefault()?.Trim();
            if (firstPart is null)
                return string.Empty;

            // Create list of payee types
            // Todo: move to appsettings
            List<(string Name, int? Index1, int? Index2, string? FallBackString, Func<string, string, (string payeeIndex1, string payeeIndex2)>? OverwriteFunction)> listPayeeTypes = new List<(string, int?, int?, string?, Func<string, string, (string, string)>?)>
            {
                ("EUROPESE DOMICILIERING VAN", 1, 7, null, OverwriteEuropeseDomicilieringVan),
                ("MAANDELIJKSE BIJDRAGE", 1, null, null, null),
                ("OVERSCHRIJVING IN EURO VAN REKENING", 2, null, null, null),
                ("OVERSCHRIJVING IN EURO OP REKENING", 3, null, null, null),
                ("MOBIELE BETALING", null, null, "MOBIELE BETALING (P2P)", null),
                ("STORTING VAN", 1, null, null, null),
                ("BETALING AAN BANK CARD COMPANY", 1, null, null, null),
                ("TERUGBETALING WOONKREDIET", null, null, "TERUGBETALING WOONKREDIET", null),
                ("BETALING MET DEBETKAART", 3, null, null, OverwriteBetalingMetDebetkaart),
                ("WERO OVERSCHRIJVING IN EURO", 1, 2, null, OverwriteWeroOverschrijvingInEuro),
                ("GELDOPNEMING IN EURO", 4, null, null, OverwriteGeldOpnemingInEuro)
            };
            
            // Find payee type and extra payee from narrative string array
            foreach ( var payeeType in listPayeeTypes)
            {
                if(firstPart.Equals(payeeType.Name, StringComparison.InvariantCultureIgnoreCase))
                {
                    if (!payeeType.Index1.HasValue && !payeeType.Index2.HasValue && !string.IsNullOrWhiteSpace(payeeType.FallBackString))
                        return payeeType.FallBackString;

                    var result  = GetPayeeFromArray(payeeType.Index1, payeeType.Index2, stringArray, payeeType.OverwriteFunction);
                    if (!string.IsNullOrWhiteSpace(result))
                    {
                        return result;
                    }
                }
            }

            // final fallback
            if (stringArray.Count >= 3)
                return stringArray.ElementAt(3 - 1);

            // Return empty string if fallback fails
            return string.Empty;
        }

        private string GetPayeeFromArray(int? index1, int? index2, List<string> strings, Func<string, string, (string, string)>? OverwriteFunction)
        {
            string resultIndex1 = ((index1.HasValue && strings.Count >= (index1.Value + 1)) ? strings.ElementAt(index1.Value) : string.Empty) ?? string.Empty;
            string resultIndex2 = ((index2.HasValue && strings.Count >= (index2.Value + 1)) ? strings.ElementAt(index2.Value) : string.Empty) ?? string.Empty;

            if (OverwriteFunction is not null)
            {
                var resultOverwriteFunction = OverwriteFunction(resultIndex1, resultIndex2);
                resultIndex1 = resultOverwriteFunction.Item1;
                resultIndex2 = resultOverwriteFunction.Item2;
            }

            var results = new[] { resultIndex1.Trim(), resultIndex2.Trim() };
            var result = string.Join(" - ", results.Where(r => !string.IsNullOrWhiteSpace(r)));
            return result;
        }

        private (string, string) OverwriteWeroOverschrijvingInEuro(string payeeIndex1, string payeeIndex2)
        {
            // example 1
            // input: "LastName FirtName" and "BE94 #### #### ####"
            // output: "LastName FirstName" and "BE94 #### #### ####"
            //  nothing happened

            // example 1
            // input: "BE94 #### #### ####  BIC GEBABEBBXXX" and "LastName FirtName"
            // output: "LastName FirstName" and "BE94 #### #### ####"
            // swap inputs and cleanup accountnumber

            if (payeeIndex1.StartsWith("BE") && payeeIndex1.Contains("BIC"))
            {           
                // cleanup
                if(payeeIndex1.IndexOf("BIC") > -1)
                    payeeIndex1 = payeeIndex1.Substring(0, payeeIndex1.IndexOf("BIC")).Trim();

                // swap 
                var temp = payeeIndex1;
                payeeIndex1 = payeeIndex2;
                payeeIndex2 = temp;
            }

            return (payeeIndex1, payeeIndex2);
        }

        private (string, string) OverwriteEuropeseDomicilieringVan(string payeeIndex1, string payeeIndex2)
        {
            // example1
            // input: "TELENET BV" and "ACCOUNT: ############## REF : ###########"
            // output: "TELENET BV" and "ACCOUNT: ##############"
            // cleanup TELENET BV, ref not needed

            // example2
            // input: "Insurance123" and "FIRE INSURANCE 123-#######-90 10/01"
            // output: "Insurance123" and "FIRE INSURANCE"
            // cleanup extra info about the payment

            if (payeeIndex1.Equals("TELENET BV", StringComparison.InvariantCultureIgnoreCase))
            {
                var index = payeeIndex2.IndexOf("REF");
                if(index > -1)
                    payeeIndex2 = payeeIndex2.Substring(0, index).Trim();
            }
            else
            {
                payeeIndex2 = RemoveDigits(payeeIndex2);
                payeeIndex2 = RemoveTrailingCharacters(payeeIndex2, ['-', '/', ' ']);
            }

            return (payeeIndex1, payeeIndex2);
        }

        private (string, string) OverwriteBetalingMetDebetkaart(string payeeIndex1, string payeeIndex2)
        {
            // example1
            // input: "SPOTIFY P##########" and ""
            // output: "SPOTIFY" and ""
            // cleanup payee

            if (payeeIndex1.Contains("SPOTIFY P", StringComparison.InvariantCultureIgnoreCase))
            {
                payeeIndex1 = "SPOTIFY";
            }

            return (payeeIndex1, payeeIndex2);
        }

        private (string, string) OverwriteGeldOpnemingInEuro(string payeeIndex1, string payeeIndex2)
        {
            // example1
            // input: "BC CASH CASH STA  CITY" and ""
            // output: "CASH WITHDRAWAL" and "C CASH CASH STA  CITY"
            // reorder and add info (only had this type once, so not sure if anything else needs to be overwritten)

            payeeIndex2 = payeeIndex1;
            payeeIndex1 = "CASH WITHDRAWAL";
         
            return (payeeIndex1, payeeIndex2);
        }     

        private string RemoveDigits(string text)
        {
            text = String.Concat(text.Where(c => !Char.IsDigit(c)));
            return text;
        }

        private string RemoveTrailingCharacters(string text, char[] characters)
        {
            return text.Trim(characters);
        }
    }   
}
