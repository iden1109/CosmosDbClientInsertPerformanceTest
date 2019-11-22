using System.Collections.Generic;
using System.Linq;
using System.Security.Authentication;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace DocDbClient
{
    using MongoDB.Bson;
    using System;
    using System.Net;
    using System.Threading.Tasks;


    public class CosmosWriteOperations<T> where T : class
    {
        private readonly DbLocation dataLocation;
        private readonly CosmosDbConfiguration cosmosDbConfiguration;
        private readonly MongoClient documentClient;
        private IMongoCollection<T> container;


        public CosmosWriteOperations(DbLocation dataLocation,
            CosmosDbConfiguration cosmosDbConfiguration)
        {
            this.dataLocation = dataLocation;
            this.cosmosDbConfiguration = cosmosDbConfiguration;

            string connectionString = @"mongodb://cosmosdb-wu2-prod1:57Xdf1TIvDC7Qfw7mGvJNSHBKb0lGnflkKz4mhDR9o2pHwG6mShmdHfFifCAqC8uuPdFJB8TCODLMUPySm0aOQ==@cosmosdb-wu2-prod1.documents.azure.com:10255/?ssl=true&replicaSet=globaldb";
            //string connectionString = @"mongodb://MiddleLayerApi:4rmaFAT@localhost:27017";
            MongoClientSettings settings = MongoClientSettings.FromUrl(
                new MongoUrl(connectionString)
            );
            settings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
            this.documentClient = new MongoClient(settings);
        }

        public async Task AddDocumentInCollection(T document)
        {
            await this.container.InsertOneAsync(document);

        }

        public async Task<T> GetDocumentInCollection(string key)
        {
            FilterDefinition<T> filter = Builders<T>.Filter.Eq("key", key);
            IEnumerable<T> entity = null;
            using (IAsyncCursor<T> cursor = await this.container.FindAsync(filter))
            {
                while (await cursor.MoveNextAsync())
                {
                    entity = cursor.Current;
                }
            }

            return entity.FirstOrDefault();
        }

        public async Task CreateDocumentCollectionIfNotExists()
        {
            CreateCollection();
        }

        public async Task CleanupDocumentCollection()
        {
            Console.WriteLine($"Deleting Database {this.dataLocation.DatabaseId}");
            await this.documentClient.DropDatabaseAsync(this.dataLocation.DatabaseId);
        }


        private void CreateCollection()
        {
            var database = this.documentClient.GetDatabase(this.dataLocation.DatabaseId);
            this.container = database.GetCollection<T>(this.dataLocation.CollectionId);
        }

    }
}