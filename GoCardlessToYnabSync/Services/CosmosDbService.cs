using GoCardlessToYnabSync.Models;
using GoCardlessToYnabSync.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using System;

namespace GoCardlessToYnabSync.Services
{
    public class CosmosDbService
    {
        private readonly IConfiguration _configuration;
        public CosmosDbService(IConfiguration configuration) 
        {
            _configuration = configuration;
        }

        public async Task<List<Transaction>> GetTransactionsNoSynced()
        {
            var cosmosDbOptions = new CosmosDbOptions();
            _configuration.GetSection(CosmosDbOptions.CosmosDb).Bind(cosmosDbOptions);

            var client = await GetCosmosClient(cosmosDbOptions);
            var container = client.GetContainer(cosmosDbOptions.Database, cosmosDbOptions.ContainerTransactions);

            using FeedIterator<Transaction> feed = container.GetItemQueryIterator<Transaction>(
                queryText: "SELECT * FROM Transactions t where t.syncedOn = null"
            );

            var transactions = new List<Transaction>();
            while (feed.HasMoreResults)
            {
                FeedResponse<Transaction> response = await feed.ReadNextAsync();

                foreach (Transaction item in response)
                {
                    transactions.Add(item);
                }
            }
            return transactions;
        }

        public async Task<List<Transaction>> GetTransactionsSince(DateTime dateTime)
        {
            var cosmosDbOptions = new CosmosDbOptions();
            _configuration.GetSection(CosmosDbOptions.CosmosDb).Bind(cosmosDbOptions);

            var client = await GetCosmosClient(cosmosDbOptions);
            var container = client.GetContainer(cosmosDbOptions.Database, cosmosDbOptions.ContainerTransactions);

            var parameterizedQuery = new QueryDefinition(
                query: "SELECT * FROM Transactions t where t.bookingDate >  @bookedAfter"
            ).WithParameter("@bookedAfter", dateTime);

            using FeedIterator<Transaction> feed = container.GetItemQueryIterator<Transaction>(
                queryDefinition: parameterizedQuery
            );

            var transactions = new List<Transaction>();
            while (feed.HasMoreResults)
            {
                FeedResponse<Transaction> response = await feed.ReadNextAsync();

                foreach (Transaction item in response)
                {
                    transactions.Add(item);
                }
            }
            return transactions;
        }

        public async Task AddOrUpdateTransactions(List<Transaction> transactions)
        {
            var cosmosDbOptions = new CosmosDbOptions();
            _configuration.GetSection(CosmosDbOptions.CosmosDb).Bind(cosmosDbOptions);

            var client = await GetCosmosClient(cosmosDbOptions);
            var container = client.GetContainer(cosmosDbOptions.Database, cosmosDbOptions.ContainerTransactions);

            foreach (Transaction item in transactions)
            {
                var resp = await container.UpsertItemAsync(item);
            }
        }

        public async Task<Requisition> UpdateRequistion(Requisition requisition)
        {
            var cosmosDbOptions = new CosmosDbOptions();
            _configuration.GetSection(CosmosDbOptions.CosmosDb).Bind(cosmosDbOptions);

            var client = await GetCosmosClient(cosmosDbOptions);
            var container = client.GetContainer(cosmosDbOptions.Database, cosmosDbOptions.ContainerRequisitions);

            var resp = await container.UpsertItemAsync(requisition);
            return requisition;
        }

        public async Task AddNewRequisition(Requisition requisition)
        {
            var cosmosDbOptions = new CosmosDbOptions();
            _configuration.GetSection(CosmosDbOptions.CosmosDb).Bind(cosmosDbOptions);

            var client = await GetCosmosClient(cosmosDbOptions);
            var container = client.GetContainer(cosmosDbOptions.Database, cosmosDbOptions.ContainerRequisitions);

            var resp = await container.CreateItemAsync(requisition);
        }

        public async Task<Requisition?> GetLastRequisitionId ()
        {
            var cosmosDbOptions = new CosmosDbOptions();
            _configuration.GetSection(CosmosDbOptions.CosmosDb).Bind(cosmosDbOptions);

            var client = await GetCosmosClient(cosmosDbOptions);
            var container = client.GetContainer(cosmosDbOptions.Database, cosmosDbOptions.ContainerRequisitions);

            using FeedIterator<Requisition> feed = container.GetItemQueryIterator<Requisition>(
                queryText: "SELECT top 1 * FROM Requisition r WHERE r.valid or IS_NULL(r.valid) ORDER BY r.CreatedOn DESC"
            );

            Requisition? lastRequisition = null;
            while (feed.HasMoreResults)
            {
                FeedResponse<Requisition> response = await feed.ReadNextAsync();

                foreach (Requisition item in response)
                {
                    lastRequisition = item;
                    if (lastRequisition != null)
                    {
                        return lastRequisition;
                    }
                }
            }
            return lastRequisition;
        }

        private async Task<CosmosClient> GetCosmosClient(CosmosDbOptions cosmosDbOptions)
        {
            var client = new CosmosClient(cosmosDbOptions.ConnectionString);

            if (client == null)
                throw new ArgumentNullException(nameof(client));
            await InitCosmosDB(client, cosmosDbOptions);

            return client;
        }
        private async Task InitCosmosDB(CosmosClient client, CosmosDbOptions cosmosDbOptions)
        {
            var respDatabse = await client.CreateDatabaseIfNotExistsAsync(cosmosDbOptions.Database, 400);
            var database = client.GetDatabase(cosmosDbOptions.Database);
            var respContainerT = await database.CreateContainerIfNotExistsAsync(cosmosDbOptions.ContainerTransactions, "/bookingDate");
            var respContainerR = await database.CreateContainerIfNotExistsAsync(cosmosDbOptions.ContainerRequisitions, "/requisitionId");
        }
    }
}
