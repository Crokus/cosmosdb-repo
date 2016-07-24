using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Client.TransientFaultHandling;
using System.Threading.Tasks;

namespace DocumentDB.Repository
{
    public interface IDocumentDbInitializer
    {
        IReliableReadWriteDocumentClient GetClient(string endpointUrl, string authorizationKey, ConnectionPolicy connectionPolicy = null);
    }
}