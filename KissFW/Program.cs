using HapLib;
using Infrastructure.Interfaces;
using Infrastructure.Models;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
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
                    Console.WriteLine($"<<<{webpage.Url}>>>");
                    try
                    {
                        await download.FetchFileAsync(webpage);
                    }
                    catch (Exception excp)
                    {
                        Console.WriteLine($"Main EXCEPTION\t{excp.Message}");   // see Filespec like '~%'
                        webpage.NeedDownload = false;
                        continue;
                    }
                }
                list15 = await repo.GetWebPagesToDownloadAsync();               // get next batch
            }
            var finalcnt = await repo.SaveChangesAsync();                       // final flush to SQL (update any "webpage.NeedDownload = false" rows)
            Console.WriteLine("*** FINISHED ***");
            foreach (var extn in MimeCollection.MissingExtns.OrderBy(e => e))
            {
                Console.WriteLine($"missing extn\t{extn}");
            }
            Console.ReadLine();
        }
    }
}
