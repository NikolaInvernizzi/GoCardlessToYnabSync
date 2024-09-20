using GoCardlessToYnabSync.Models;
using GoCardlessToYnabSync.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;

namespace GoCardlessToYnabSync.Services
{
    public class CosmosDbService
    {
        private readonly CosmosDbOptions _cosmosDbOptions;
        private readonly CosmosClient _cosmosClient;

        public CosmosDbService(IOptions<CosmosDbOptions> cosmosDbOptions) 
        {
            _cosmosDbOptions = cosmosDbOptions.Value;

            _cosmosClient = new CosmosClient(_cosmosDbOptions.ConnectionString);

            if (_cosmosClient == null)
                throw new ArgumentNullException(nameof(_cosmosClient));
        }

        public async Task<List<Transaction>> GetTransactionsNoSynced()
        {
            var container = await GetContainerInitialized(_cosmosDbOptions.ContainerTransactions, _cosmosDbOptions.ContainerTransactionsPartitionKey);

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
            var container = await GetContainerInitialized(_cosmosDbOptions.ContainerTransactions, _cosmosDbOptions.ContainerTransactionsPartitionKey);

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
            var container = await GetContainerInitialized(_cosmosDbOptions.ContainerTransactions, _cosmosDbOptions.ContainerTransactionsPartitionKey);

            foreach (Transaction item in transactions)
            {
                var resp = await container.UpsertItemAsync(item);
            }
        }

        public async Task<Requisition> UpdateRequistion(Requisition requisition)
        {
            var container = await GetContainerInitialized(_cosmosDbOptions.ContainerRequisitions, _cosmosDbOptions.ContainerRequisitionsPartitionKey);

            var resp = await container.UpsertItemAsync(requisition);
            return requisition;
        }

        public async Task AddNewRequisition(Requisition requisition)
        {
            var container = await GetContainerInitialized(_cosmosDbOptions.ContainerRequisitions, _cosmosDbOptions.ContainerRequisitionsPartitionKey);

            var resp = await container.CreateItemAsync(requisition);
        }

        public async Task<Requisition?> GetLastRequisitionId ()
        {
            var container = await GetContainerInitialized(_cosmosDbOptions.ContainerRequisitions, _cosmosDbOptions.ContainerRequisitionsPartitionKey);

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

        public async Task<List<Requisition>> GetAllRequistionIds()
        {
            var container = await GetContainerInitialized(_cosmosDbOptions.ContainerRequisitions, _cosmosDbOptions.ContainerRequisitionsPartitionKey);

            using FeedIterator<Requisition> feed = container.GetItemQueryIterator<Requisition>(
                queryText: "SELECT top 1 * FROM Requisition r ORDER BY r.CreatedOn DESC"
            );

            var reqIds = new List<Requisition>();
            while (feed.HasMoreResults)
            {
                FeedResponse<Requisition> response = await feed.ReadNextAsync();

                foreach (Requisition item in response)
                {
                    reqIds.Add(item);
                }
            }
            return reqIds;
        }

        private async Task<Container> GetContainerInitialized(string containerId, string partionKey)
        {
            var respDatabse = await _cosmosClient.CreateDatabaseIfNotExistsAsync(_cosmosDbOptions.Database, 400);
            var database = _cosmosClient.GetDatabase(_cosmosDbOptions.Database);
            var containerResp = await database.CreateContainerIfNotExistsAsync(containerId, partionKey);
            return containerResp.Container;
        }
    }
}
