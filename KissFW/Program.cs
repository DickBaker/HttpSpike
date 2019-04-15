using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HapLib;
using Infrastructure.Interfaces;
using Infrastructure.Models;
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
            if (!int.TryParse(ConfigurationManager.AppSettings["batchsize"], out var batchSize))
            {
                batchSize = 15;
            }
            if (!Directory.Exists(htmldir))
            {
                Directory.CreateDirectory(htmldir);
            }
            if (!Directory.Exists(otherdir))
            {
                Directory.CreateDirectory(otherdir);
            }

            var download = new Downloader(HParser, repo, htmldir, otherdir);

            var batch = await repo.GetWebPagesToDownloadAsync(batchSize);      // get first batch (as IList<WebPage>)
            while (batch.Count > 0)
            {
                foreach (var webpage in batch)                                 // iterate through [re-]obtained List
                {
                    Console.WriteLine($"<<<{webpage.Url}>>>");
                    try
                    {
                        await download.FetchFileAsync(webpage);                 // complete current page before starting the next
                    }
                    catch (Exception excp)                                      // either explicit from FetchFileAsync or HTTP timeout [TODO: Polly retries]
                    {
                        Console.WriteLine($"Main EXCEPTION\t{excp.Message}");   // see Filespec like '~%'
                        webpage.NeedDownload = false;                           // prevent any [infinite] retry loop
                    }
                }
                var finalcnt = await repo.SaveChangesAsync();                   // flush to update any pending "webpage.NeedDownload = false" rows (else p_ToDownload will repeat)
                batch = await repo.GetWebPagesToDownloadAsync(batchSize);       // get next batch
            }
            Console.WriteLine("*** FINISHED ***");
            foreach (var extn in MimeCollection.MissingExtns.OrderBy(e => e))
            {
                Console.WriteLine($"missing extn\t{extn}");
            }
            Console.ReadLine();
        }
    }
}
