using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tests
{
    [CollectionDefinition("DatabaseTests")]
    public class DatabaseTestsCollection : ICollectionFixture<CustomWebApplicationFactory>
    {
        // Класс-маркер для группировки тестов
    }
}
