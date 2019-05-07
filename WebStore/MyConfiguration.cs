using System;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.SqlServer;

namespace WebStore
{
    public class MyConfiguration : DbConfiguration
    {
        public MyConfiguration()
        {
            SetTransactionHandler(SqlProviderServices.ProviderInvariantName, () => new CommitFailureHandler());
            SetExecutionStrategy("System.Data.SqlClient", () => new SqlAzureExecutionStrategy(5, TimeSpan.FromSeconds(5)));
        }
    }
}
