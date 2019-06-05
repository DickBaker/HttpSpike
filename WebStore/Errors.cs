using System;
using System.Data;
using System.Data.Entity.Infrastructure;
using System.Threading.Tasks;

namespace WebStore
{
    static class Errors
    {
        public static void BombIf(this Task t)
        {
            if (t.IsFaulted)
            {
                throw new Exception($"task failed with exception {t.Exception}");
            }
        }
        public static void WaitBombIf(this Task t)
        {
            try
            {
                t.Wait();
                if (t.IsFaulted)            // should never happen as prev statement should throw exception
                {
                    throw new Exception($"task failed with DODGY exception {t.Exception}");
                }

            }
            catch (RetryLimitExceededException excp)        // cf. https://docs.microsoft.com/en-us/aspnet/mvc/overview/getting-started/getting-started-with-ef-using-mvc/connection-resiliency-and-command-interception-with-the-entity-framework-in-an-asp-net-mvc-application
            {
                Console.WriteLine($"RetryLimitExceededException {excp}");
            }
            catch (DataException excp)
            {
                Console.WriteLine($"DataException {excp}");
            }
            catch (Exception excp)
            {
                Console.WriteLine($"Exception {excp}");
            }
        }
    }
}
