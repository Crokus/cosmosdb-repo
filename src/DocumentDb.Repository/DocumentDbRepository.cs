using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using DocumentDb.Repository;
using DocumentDb.Repository.Infrastructure;
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

        private readonly string _repositoryIdentityProperty = "id";
        private readonly string _defaultIdentityPropertyName = "id";

        public DocumentDbRepository(DocumentClient client, string databaseId, Func<string> collectionNameFactory = null, Expression<Func<T, object>> idNameFactory = null)
        {
            _client = client;
            _databaseId = databaseId;

            _database = new AsyncLazy<Database>(async () => await GetOrCreateDatabaseAsync());
            _collection = new AsyncLazy<DocumentCollection>(async () => await GetOrCreateCollectionAsync());

            _collectionName = collectionNameFactory != null ? collectionNameFactory() : typeof(T).Name;

            _repositoryIdentityProperty = TryGetIdProperty(idNameFactory);
        }

        private string TryGetIdProperty(Expression<Func<T, object>> idNameFactory)
        {
            Type entityType = typeof (T);            
            var properties = entityType.GetProperties();

            // search for idNameFactory
            if (idNameFactory != null && idNameFactory.Body is MemberExpression)
            {
                MemberInfo customPropertyInfo = ((MemberExpression) idNameFactory.Body).Member;
                EnsurePropertyHasJsonAttributeWithCorrectPropertyName(customPropertyInfo);

                return customPropertyInfo.Name;
            }

            // search for id property in entity
            var idProperty = properties.SingleOrDefault(p => p.Name == _defaultIdentityPropertyName);

            if (idProperty != null)
            {
                return idProperty.Name;
            }

             // search for Id property in entity
            idProperty = properties.SingleOrDefault(p => p.Name == "Id");

            if (idProperty != null)
            {
                EnsurePropertyHasJsonAttributeWithCorrectPropertyName(idProperty);

                return idProperty.Name;
            }

            // identity property not found;
            throw new ArgumentException("Unique identity property not found. Create \"id\" property for your entity or use different property name with JsonAttribute with PropertyName set to \"id\"");
        }

        private void EnsurePropertyHasJsonAttributeWithCorrectPropertyName(MemberInfo idProperty)
        {
            var attributes = idProperty.GetCustomAttributes(typeof (JsonPropertyAttribute), true);
            if (!(attributes.Length == 1 &&
                ((JsonPropertyAttribute) attributes[0]).PropertyName == _defaultIdentityPropertyName))
            {
                throw new ArgumentException(
                        string.Format(
                            "\"{0}\" property needs to be decorated with JsonAttirbute with PropertyName set to \"id\"",
                            idProperty.Name));
            }
        }

        /// <summary>
        /// Removes the underlying DocumentDB collection. NOTE: Each time you create a collection, you incur a charge for at least one hour of use, as determined by the specified performance level of the collection. 
        /// If you create a collection and delete it within an hour, you are still charged for one hour of use
        /// </summary>
        /// <returns></returns>
        public async Task<bool> RemoveAsync()
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

            // check if entity exist
            T existingEntity = await GetByIdAsync(GetId(entity));

            if (existingEntity != null)
            {
                // get doc
                Document doc = await GetDocumentByIdAsync(GetId(existingEntity));

                // update Id field if it doesn't exist
                var entityId = GetId(entity);

                if (string.IsNullOrEmpty(entityId))
                {
                    SetValue(_repositoryIdentityProperty, entity, GetId(existingEntity));
                }

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

        public async Task<long> CountAsync()
        {
            return _client.CreateDocumentQuery<T>((await _collection).SelfLink).AsEnumerable().LongCount();
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            return _client.CreateDocumentQuery<T>((await _collection).SelfLink).AsEnumerable();
        }

        public async Task<T> GetByIdAsync(string id)
        {
            return await FirstOrDefaultAsync(d => GetId(d) == id);
        }

        public async Task<T> FirstOrDefaultAsync(Func<T, bool> predicate)
        {
            return
                _client.CreateDocumentQuery<T>((await _collection).DocumentsLink)
                    .Where(predicate)
                    .AsEnumerable()
                    .FirstOrDefault();
        }

        public async Task<IQueryable<T>> WhereAsync(Expression<Func<T, bool>> predicate)
        {
            return _client.CreateDocumentQuery<T>((await _collection).DocumentsLink)
                .Where(predicate);
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
            var body = Expression.Property(p, _repositoryIdentityProperty);
            var exp = Expression.Lambda<Func<T, string>>(body, p);
            return exp.Compile()(entity);
        }

        private void SetValue<T, TV>(string propertyName, T item, TV value)
        {
            MethodInfo method = typeof(T).GetProperty(propertyName).GetSetMethod();
            Action<T, TV> setter = (Action<T, TV>)Delegate.CreateDelegate(typeof(Action<T, TV>), method);
            setter(item, value);
        }
    }
}
