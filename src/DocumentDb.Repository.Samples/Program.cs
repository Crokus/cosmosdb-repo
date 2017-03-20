using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.Linq;
using System.Threading.Tasks;
using DocumentDB.Repository;
using DocumentDb.Repository.Samples.Model;
using Microsoft.Azure.Documents.Client.TransientFaultHandling;

namespace DocumentDb.Repository.Samples
{
    internal class Program
    {
        public static IReliableReadWriteDocumentClient Client { get; set; }

        private static void Main(string[] args)
        {
            IDocumentDbInitializer init = new DocumentDbInitializer();

            string endpointUrl = ConfigurationManager.AppSettings["azure.documentdb.endpointUrl"];
            string authorizationKey = ConfigurationManager.AppSettings["azure.documentdb.authorizationKey"];

            // get the Azure DocumentDB client
            Client = init.GetClient(endpointUrl, authorizationKey);

            // Run demo
            Task t = MainAsync(args);
            t.Wait();
        }

        private static async Task MainAsync(string[] args)
        {
            string databaseId = ConfigurationManager.AppSettings["azure.documentdb.databaseId"];

            // create repository for persons and set Person.FullName property as identity field (overriding default Id property name)
            DocumentDbRepository<Person> repo = new DocumentDbRepository<Person>(Client, databaseId, null, p => p.FullName);
            
            // output all persons in our database, nothing there yet
            await PrintPersonCollection(repo);

            // create a new person
            Person matt = new Person
            {
                FirstName = "m4tt",
                LastName = "TBA",
                BirthDayDateTime = new DateTime(1990, 10, 10),
                PhoneNumbers =
                    new Collection<PhoneNumber>
                    {
                        new PhoneNumber {Number = "555", Type = "Mobile"},
                        new PhoneNumber {Number = "777", Type = "Landline"}
                    }
            };

            // add person to database's collection (if collection doesn't exist it will be created and named as class name -it's a convenction, that can be configured during initialization of the repository)
            matt = await repo.AddOrUpdateAsync(matt);

            // create another person
            Person jack = new Person
            {
                FirstName = "Jack",
                LastName = "Smith",
                BirthDayDateTime = new DateTime(1990, 10, 10),
                PhoneNumbers = new Collection<PhoneNumber>()
            };

            // add jack to collection
            jack = await repo.AddOrUpdateAsync(jack);

            // should output person and his two phone numbers
            await PrintPersonCollection(repo);

            // change birth date
            matt.BirthDayDateTime -= new TimeSpan(500, 0, 0, 0);

            // remove landline phone number
            matt.PhoneNumbers.RemoveAt(1);

            // should update person
            await repo.AddOrUpdateAsync(matt);

            // should output Matt with just one phone number
            await PrintPersonCollection(repo);

            // get Matt by his Id
            Person justMatt = await repo.GetByIdAsync(matt.FullName);
            Console.WriteLine("GetByIdAsync result: " + justMatt);

            // ... or by his first name
            Person firstMatt = await repo.FirstOrDefaultAsync(p => p.FirstName.Equals("matt", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("First: " + firstMatt);

            // query all the smiths
            var smiths = (await repo.WhereAsync(p => p.LastName.Equals("Smith"))).ToList();
            Console.WriteLine(smiths.Count);

            // use IQueryable, as for now supported expressions are 'Queryable.Where', 'Queryable.Select' & 'Queryable.SelectMany'
            var allSmithsPhones =
                (await repo.QueryAsync()).SelectMany(p => p.PhoneNumbers).Select(p => p.Type);
            foreach (var phone in allSmithsPhones)
            {
                Console.WriteLine(phone);
            }

            // count all persons
            var personsCount = await repo.CountAsync();

            // count all jacks
            var jacksCount = await repo.CountAsync(p => p.FirstName == "Jack");

            // remove matt from collection
            await repo.RemoveAsync(matt.FullName);

            // remove jack from collection
            await repo.RemoveAsync(jack.FullName);

            // should output nothing
            await PrintPersonCollection(repo);
            
            // remove collection
            await repo.RemoveAsync();
        }

        private static async Task PrintPersonCollection(DocumentDbRepository<Person> repo)
        {
            IEnumerable<Person> persons = await repo.GetAllAsync();

            persons.ToList().ForEach(Console.WriteLine);
        }
    }
}