using DocumentDb.Repository.Infrastructure;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Client.TransientFaultHandling;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;

namespace DocumentDB.Repository
{
    public class DocumentDbRepository<T> : IDocumentDbRepository<T> where T : class
    {
        private readonly IReliableReadWriteDocumentClient _client;
        private readonly string _databaseId;

        private readonly AsyncLazy<Database> _database;
        private AsyncLazy<DocumentCollection> _collection;

        private readonly string _collectionName;

        private readonly string _repositoryIdentityProperty = "id";
        private readonly string _defaultIdentityPropertyName = "id";

        public DocumentDbRepository(IReliableReadWriteDocumentClient client, string databaseId, Func<string> collectionNameFactory = null, Expression<Func<T, object>> idNameFactory = null)
        {
            _client = client;
            _databaseId = databaseId;

            _database = new AsyncLazy<Database>(async () => await GetOrCreateDatabaseAsync());
            _collection = new AsyncLazy<DocumentCollection>(async () => await GetOrCreateCollectionAsync());

            _collectionName = collectionNameFactory != null ? collectionNameFactory() : typeof(T).Name;

            _repositoryIdentityProperty = TryGetIdProperty(idNameFactory);
        }

        /// <summary>
        /// Removes the underlying DocumentDB collection. NOTE: Each time you create a collection, you incur a charge for at least one hour of use, as determined by the specified performance level of the collection. 
        /// If you create a collection and delete it within an hour, you are still charged for one hour of use
        /// </summary>
        /// <returns></returns>
        public async Task<bool> RemoveAsync(RequestOptions requestOptions = null)
        {
            var result = await _client.DeleteDocumentCollectionAsync((await _collection).SelfLink, requestOptions);

            bool isSuccess = result.StatusCode == HttpStatusCode.NoContent;

            _collection = new AsyncLazy<DocumentCollection>(async () => await GetOrCreateCollectionAsync());

            return isSuccess;
        }

        public async Task<bool> RemoveAsync(string id, RequestOptions requestOptions = null)
        {
            bool isSuccess = false;

            var doc = await GetDocumentByIdAsync(id);

            if (doc != null)
            {
                var result = await _client.DeleteDocumentAsync(doc.SelfLink, requestOptions);

                isSuccess = result.StatusCode == HttpStatusCode.NoContent;
            }

            return isSuccess;
        }

        public async Task<T> AddOrUpdateAsync(T entity, RequestOptions requestOptions = null)
        {
            T upsertedEntity;

            var upsertedDoc = await _client.UpsertDocumentAsync((await _collection).SelfLink, entity, requestOptions);
            upsertedEntity = JsonConvert.DeserializeObject<T>(upsertedDoc.Resource.ToString());

            return upsertedEntity;
        }

        public async Task<int> CountAsync()
        {
            return _client.CreateDocumentQuery<T>((await _collection).SelfLink).Count();
        }

        public async Task<int> CountAsync(Expression<Func<T, bool>> predicate)
        {
            return _client.CreateDocumentQuery<T>((await _collection).SelfLink).Where(predicate).Count();
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            return _client.CreateDocumentQuery<T>((await _collection).SelfLink).AsEnumerable();
        }

        public async Task<T> GetByIdAsync(string id)
        {
            var retVal = await GetDocumentByIdAsync(id);
            return (T)(dynamic)retVal;
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

        private string TryGetIdProperty(Expression<Func<T, object>> idNameFactory)
        {
            Type entityType = typeof(T);
            var properties = entityType.GetProperties();

            // search for idNameFactory
            if (idNameFactory != null)
            {
                var expr = GetMemberExpression(idNameFactory);
                MemberInfo customPropertyInfo = expr.Member;

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
            var attributes = idProperty.GetCustomAttributes(typeof(JsonPropertyAttribute), true);
            if (!(attributes.Length == 1 &&
                ((JsonPropertyAttribute)attributes[0]).PropertyName == _defaultIdentityPropertyName))
            {
                throw new ArgumentException(
                        string.Format(
                            "\"{0}\" property needs to be decorated with JsonAttirbute with PropertyName set to \"id\"",
                            idProperty.Name));
            }
        }

        private async Task<Document> GetDocumentByIdAsync(object id)
        {
            return _client.CreateDocumentQuery<Document>((await _collection).SelfLink).Where(d => d.Id == id.ToString()).AsEnumerable().FirstOrDefault();
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

        private object GetId(T entity)
        {
            var p = Expression.Parameter(typeof(T), "x");
            Expression body = Expression.Property(p, _repositoryIdentityProperty);
            if (body.Type.IsValueType)
            {
                body = Expression.Convert(body, typeof(object));
            }
            var exp = Expression.Lambda<Func<T, object>>(body, p);
            return exp.Compile()(entity);
        }

        private void SetValue<T, TV>(string propertyName, T item, TV value)
        {
            MethodInfo method = typeof(T).GetProperty(propertyName).GetSetMethod();
            Action<T, TV> setter = (Action<T, TV>)Delegate.CreateDelegate(typeof(Action<T, TV>), method);
            setter(item, value);
        }

        private MemberExpression GetMemberExpression<T>(Expression<Func<T, object>> expr)
        {
            var member = expr.Body as MemberExpression;
            var unary = expr.Body as UnaryExpression;
            return member ?? (unary != null ? unary.Operand as MemberExpression : null);
        }
    }
}
