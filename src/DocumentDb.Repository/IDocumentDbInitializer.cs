using Microsoft.Azure.Documents.Client;

namespace DocumentDB.Repository
{
    public interface IDocumentDbInitializer
    {
        DocumentClient GetClient(string endpointUrl, string authorizationKey, ConnectionPolicy connectionPolicy = null);
    }
}