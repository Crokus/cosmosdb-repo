using System;
using Microsoft.Azure.Documents.Client;

namespace DocumentDB.Repository
{
    public class DocumentDbInitializer : IDocumentDbInitializer
    {
        public DocumentClient GetClient(string endpointUrl, string authorizationKey)
        {
            if (string.IsNullOrWhiteSpace(endpointUrl))
                throw new ArgumentNullException("endpointUrl");

            if (string.IsNullOrWhiteSpace(authorizationKey))
                throw new ArgumentNullException("authorizationKey");

            return new DocumentClient(new Uri(endpointUrl), authorizationKey);
        }
    }
}