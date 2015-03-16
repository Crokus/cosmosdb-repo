using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using DocumentDb.Repository;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Newtonsoft.Json;


namespace DocumentDB.Repository
{
    public class DocumentDbRepository<T> where T : class
    {
        private readonly DocumentClient _client;
        private readonly string _databaseId;

        private readonly AsyncLazy<Database> _database;
        private AsyncLazy<DocumentCollection> _collection;

        private readonly string _collectionName;
        private readonly string _idFieldName;

        public DocumentDbRepository(DocumentClient client, string databaseId, Func<string> collectionNameFactory = null, Func<string> idNameFactory = null)
        {
            _client = client;
            _databaseId = databaseId;

            _database = new AsyncLazy<Database>(async () => await GetOrCreateDatabaseAsync());
            _collection = new AsyncLazy<DocumentCollection>(async () => await GetOrCreateCollectionAsync());

            _collectionName = collectionNameFactory != null ? collectionNameFactory() : typeof(T).Name;
            _idFieldName = idNameFactory != null ? idNameFactory() : "Id";
        }

        public async Task<bool> ClearAsync()
        {
            var result = await _client.DeleteDocumentCollectionAsync((await _collection).SelfLink);

            bool isSuccess = result.StatusCode == HttpStatusCode.NoContent;

            _collection = new AsyncLazy<DocumentCollection>(async () => await GetOrCreateCollectionAsync());

            return isSuccess;
        }

        public async Task<bool> RemoveAsync(string id)
        {
            bool isSuccess = false;

            var doc = await GetDocumentByIdAsync(id);
            
            if (doc != null)
            {
                var result = await _client.DeleteDocumentAsync(doc.SelfLink);

                isSuccess = result.StatusCode == HttpStatusCode.NoContent;
            }

            return isSuccess;
        }

        public async Task<T> AddOrUpdateAsync(T entity)
        {
            T upsertedEntity;
            Document doc = await GetDocumentByIdAsync(GetId(entity));

            if (doc != null)
            {
                var updatedDoc = await _client.ReplaceDocumentAsync(doc.SelfLink, entity);
                upsertedEntity = JsonConvert.DeserializeObject<T>(updatedDoc.Resource.ToString());
            }
            else
            {
                var addedDoc = await _client.CreateDocumentAsync((await _collection).SelfLink, entity);

                upsertedEntity = JsonConvert.DeserializeObject<T>(addedDoc.Resource.ToString());
            }

            return upsertedEntity;
        }

        public async Task<int> CountAsync()
        {
            return _client.CreateDocumentQuery<T>((await _collection).SelfLink).AsEnumerable().Count();
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            return _client.CreateDocumentQuery<T>((await _collection).SelfLink).AsEnumerable();
        }

        public async Task<T> GetByIdAsync(string id)
        {
            return await FirstOrDefaultAsync(d => GetId(d) == id);
        }

        public async Task<T> FirstOrDefaultAsync(Func<T,bool> predicate)
        {
            return
                _client.CreateDocumentQuery<T>((await _collection).DocumentsLink)
                    .Where(predicate)
                    .AsEnumerable()
                    .FirstOrDefault();
        }

        public async Task<IQueryable<T>> WhereAsync(Expression<Func<T, bool>> predicate)
        {
            return _client.CreateDocumentQuery<T>((await _collection).DocumentsLink).Where(predicate);
        }

        public async Task<IQueryable<T>> QueryAsync()
        {
            return _client.CreateDocumentQuery<T>((await _collection).DocumentsLink);
        }

        private async Task<Document> GetDocumentByIdAsync(string id)
        {
            return _client.CreateDocumentQuery<Document>((await _collection).SelfLink).Where(d => d.Id == id).AsEnumerable().FirstOrDefault();
        }

        private async Task<DocumentCollection> GetOrCreateCollectionAsync()
        {
            DocumentCollection collection = _client.CreateDocumentCollectionQuery((await _database).SelfLink).Where(c => c.Id == _collectionName).ToArray().FirstOrDefault();

            if (collection == null)
            {
                collection = new DocumentCollection { Id = _collectionName };

                collection = await _client.CreateDocumentCollectionAsync((await _database).SelfLink, collection);
            }

            return collection;
        }

        private async Task<Database> GetOrCreateDatabaseAsync()
        {
            Database database = _client.CreateDatabaseQuery()
                .Where(db => db.Id == _databaseId).ToArray().FirstOrDefault();
            if (database == null)
            {
                database = await _client.CreateDatabaseAsync(
                    new Database { Id = _databaseId });
            }

            return database;
        }

        private string GetId(T entity)
        {
            var p = Expression.Parameter(typeof(T), "x");
            var body = Expression.Property(p, _idFieldName);
            var exp = Expression.Lambda<Func<T, string>>(body, p);
            return exp.Compile()(entity);
        }
    }
}
