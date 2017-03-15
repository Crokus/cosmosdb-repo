using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Client.TransientFaultHandling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace DocumentDB.Repository
{
    public interface IDocumentDbRepository<T> where T : class
    {
        Task<bool> RemoveAsync(RequestOptions requestOptions = null);

        Task<bool> RemoveAsync(string id, RequestOptions requestOptions = null);

        Task<T> AddOrUpdateAsync(T entity, RequestOptions requestOptions = null);

        Task<long> CountAsync();

        Task<IEnumerable<T>> GetAllAsync();

        Task<T> GetByIdAsync(string id);

        Task<T> FirstOrDefaultAsync(Func<T, bool> predicate);

        Task<IQueryable<T>> WhereAsync(Expression<Func<T, bool>> predicate);

        Task<IQueryable<T>> QueryAsync();
    }
}
