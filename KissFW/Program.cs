using HapLib;
using Infrastructure.Interfaces;
using Infrastructure.Models;
using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using Webstore;
using WebStore;

namespace KissFW
{
    static class Program
    {
        const string OTHFOLDER = "assets";
        static IHttpParser HParser;

        static async Task Main(string[] _)
        {
            var dbctx = new WebModel();
            //IRepository repo = new Repository(dbctx);
            IRepository repo = new BulkRepository(dbctx);
            MimeCollection.Load(await repo.GetContentTypeToExtnsAsync());

            HParser = new HapParser();
            //var ct = new CancellationToken();
            var htmldir = ConfigurationManager.AppSettings["htmldir"] ?? @"C:\Ligonier\webcache";
            var otherdir = ConfigurationManager.AppSettings["otherdir"] ?? (htmldir + Path.DirectorySeparatorChar + OTHFOLDER);
            if (!Directory.Exists(htmldir))
            {
                Directory.CreateDirectory(htmldir);
            }
            if (!Directory.Exists(otherdir))
            {
                Directory.CreateDirectory(otherdir);
            }

            var download = new Downloader(HParser, repo, htmldir, otherdir);

            var list15 = await repo.GetWebPagesToDownloadAsync();   // get first batch (as IList<WebPage>)
            while (list15.Count > 0)
            {
                foreach (var webpage in list15)                     // iterate through [re-]obtained List
                {
                    try
                    {
                        await download.FetchFileAsync(webpage);
                    }
                    catch (Exception excp)
                    {
                        Console.WriteLine(excp.Message);
                        continue;
                    }
                }
                list15 = await repo.GetWebPagesToDownloadAsync();   // get next batch
            }
            await repo.SaveChangesAsync();                          // final flush to SQL
            Console.WriteLine("*** FINISHED ***");
            Console.ReadLine();
        }
    }
}
