using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DownloadLib;
using HapLib;
using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;
using Polly;
using Webstore;
using WebStore;

namespace KissFW
{
    static class Program
    {
        const string OTHFOLDER = "assets", BACKUPFOLDER = "backup";
        enum Recovery
        {
            idle,
            gensaved,
            backupsaved,
            completed
        }

        static IHttpParser HParser;
        static string htmldir, backupdir;
        static WebModel dbctx;

        static async Task Main(string[] _)
        {

            //string fs1 = @"C:\Ligonier\webcache\state - theology - does - sin - deserve - damnation.html",
            //    fs2 = @"C:\Ligonier\webcache\assets\bible - plan.pdf";
            //var rel = Utils.GetRelativePath(fs1, fs2);
            //Console.WriteLine(rel);

            dbctx = new WebModel();             // EF context defaults to config: "name=DefaultConnection"
            //IRepository repo = new Repository(dbctx);
            IRepository repo = new BulkRepository(dbctx);

            MimeCollection.Load(await repo.GetContentTypeToExtnsAsync());

            HParser = new HapParser();
            //var ct = new CancellationToken();
            htmldir = ConfigurationManager.AppSettings["htmldir"] ?? @"C:\Ligonier\webcache";
            if (!Directory.Exists(htmldir))
            {
                Directory.CreateDirectory(htmldir);
            }
            var otherdir = ConfigurationManager.AppSettings["otherdir"] ?? (htmldir + Path.DirectorySeparatorChar + OTHFOLDER);
            if (!Directory.Exists(otherdir))
            {
                Directory.CreateDirectory(otherdir);
            }
            backupdir = ConfigurationManager.AppSettings["backupdir"] ?? (htmldir + Path.DirectorySeparatorChar + BACKUPFOLDER);
            if (!Directory.Exists(backupdir))
            {
                Directory.CreateDirectory(backupdir);
            }
            if (!int.TryParse(ConfigurationManager.AppSettings["batchsize"], out var batchSize))
            {
                batchSize = 15;
            }
            var ValidRetry = new HttpStatusCode[] {
                HttpStatusCode.Ambiguous,               // 300
                HttpStatusCode.Conflict,                // 409
                HttpStatusCode.InternalServerError,     // 500
                HttpStatusCode.NotImplemented,          // 501
                HttpStatusCode.BadGateway,              // 502
                HttpStatusCode.ServiceUnavailable,      // 503
                HttpStatusCode.GatewayTimeout };        // 504
            IAsyncPolicy<HttpResponseMessage> HttpRetryPolicy =                                // TODO: probably should configure based on App.config
                Policy.HandleResult<HttpResponseMessage>(rsp => ValidRetry.Contains(rsp.StatusCode))
                    .WaitAndRetryAsync(0, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt) / 2));  // i.e. 1, 2, 4 seconds

#pragma warning disable GCop302 // Since '{0}' implements IDisposable, wrap it in a using() statement
            //TODO: plug-in Polly as MessageProcessingHandler / whatever !
            var Client = new HttpClient(
                new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate, AllowAutoRedirect = true })
            { Timeout = new TimeSpan(0, 0, 20) };
#pragma warning restore GCop302 // Since '{0}' implements IDisposable, wrap it in a using() statement

            var download = new Downloader(repo, Client, HttpRetryPolicy, HParser, htmldir, otherdir);
            await DownloadAndParse(repo, batchSize, download);
            Console.WriteLine("*** DownloadAndParse FINISHED ***");

            var localise = new Localiser(repo, HParser, htmldir, backupdir);
            var success = await HtmlLocalise(repo, batchSize, localise);
            Console.WriteLine("*** DownloadAndParse FINISHED ***");

#if DEBUG
            foreach (var extn in MimeCollection.MissingExtns.OrderBy(e => e))
            {
                Console.WriteLine($"missing extn\t{extn}");
            }
#endif

            Console.ReadLine();
        }

        static async Task DownloadAndParse(IRepository repo, int batchSize, Downloader download)
        {
            var batch = dbctx.WebPages
                .Include("ConsumeFrom")
                .Include("SupplyTo")            // not necessary
                .Where(
                    w => w.Url.StartsWith("http://tools.ietf.org/html/rfc7230")                 // https://tools.ietf.org/html/rfc7230
                        || w.Url.StartsWith("http://www.w3.org/Protocols/rfc2616")              // https://www.w3.org/Protocols/rfc2616/rfc2616.html
                        || w.Url.StartsWith("http://www.w3schools.com/tags/ref_attributes.asp") // https://www.w3schools.com/tags/ref_attributes.asp
                    )
                .OrderBy(w => w.Url)
                .ToList();
            //var batch = dbctx.WebPages.Include("ConsumeFrom").Where(w => w.Url.StartsWith("http://amzn.to")).ToList();
            //var batch = await repo.GetWebPagesToDownloadAsync(batchSize);      // get first batch (as List<WebPage>)
            while (batch.Count > 0)
            {
                foreach (var webpage in batch)                                  // iterate through [re-]obtained List
                {
                    Console.WriteLine($"<<<{webpage.Url}>>>");
                    try
                    {

                        await download.FetchFileAsync(webpage);                 // complete current page before starting the next
                    }
                    catch (Exception excp)                                      // either explicit from FetchFileAsync or HTTP timeout [TODO: Polly retries]
                    {
                        Console.WriteLine($"Main EXCEPTION\t{excp.Message}");   // see Filespec like '~%'
                        webpage.Download = WebPage.DownloadEnum.Ignore;         // prevent any [infinite] retry loop; although Downloading table should delay
                    }
                }
                var finalcnt = await repo.SaveChangesAsync();                   // flush to update any pending webpage.Download changed rows (else p_ToDownload will repeat)
                batch = await repo.GetWebPagesToDownloadAsync(batchSize);       // get next batch
            }
        }

        /// <summary>
        ///     localise every x.html file that has been marked WebPages.Localise=2
        /// </summary>
        /// <param name="repo">
        ///     IRepository to perform database work
        /// </param>
        /// <param name="batchSize">
        ///     number of files in request from db
        /// </param>
        /// <param name="localise">
        ///     object that actually performs the localise (find+alter each link)
        /// </param>
        /// <returns>
        ///     Task although activity is heavily CPU-bound and HAP methods all sync, there is some database I/O conducted async
        /// </returns>
        /// <remarks>
        /// 1.  batchSize is set by caller [from App.config
        /// </remarks>
        static async Task<bool> HtmlLocalise(IRepository repo, int batchSize, Localiser localise)
        {
            string genfile, backupFile;
            Recovery state;
            var success = true;
            var batch = await repo.GetWebPagesToLocaliseAsync(batchSize);       // get first batch (as List<WebPage>)
            while (batch.Count > 0)
            {
                foreach (var webpage in batch)                                  // iterate through [re-]obtained List
                {
                    state = Recovery.idle;
                    var htmlFile = webpage.Filespec;
                    backupFile = backupdir + Path.DirectorySeparatorChar + Path.GetFileName(htmlFile);

                    Console.WriteLine($"<<<{webpage.Url}\t~~>\t{htmlFile }>>>");
                    try
                    {
                        var changedLinks = await localise.Translate(webpage);   // complete current page before starting the next
                        if (changedLinks)
                        {
                            /*
                            1.  save revised file to generated.xyz
                            2.  move original A.html to backup\A.html
                            3.  rename generated.xyz from A.html
                            */
                            genfile = htmldir + Path.DirectorySeparatorChar + Path.GetRandomFileName();
                            HParser.SaveFile(genfile);
                            state = Recovery.gensaved;
                            File.Move(htmlFile, backupFile);
                            state = Recovery.backupsaved;
                            File.Move(genfile, htmlFile);
                            state = Recovery.completed;

                        }
                        webpage.Localise = WebPage.LocaliseEnum.Localised;      // show Localise success
                    }
                    catch (Exception excp)                                      // either explicit from FetchFileAsync or HTTP timeout [TODO: Polly retries]
                    {
                        Console.WriteLine($"HtmlLocalise{state} EXCEPTION\t{excp.Message}");   // see Filespec like '~%'
                        switch (state)
                        {
                            case Recovery.backupsaved:                          // failed during rename genfile -> htmlFile
                                File.Move(backupFile, htmlFile);                // restore the backup to main folder (but if fails have to manually fixup later)
                                break;
                            case Recovery.idle:
                            case Recovery.gensaved:                             // move htmlFile -> backup failed (presumably still in as-was folder) so NO-OP
                            case Recovery.completed:
                            default:
                                success = false;
                                break;
                        }
                    }
                }
                var finalcnt = repo.SaveChanges();                              // flush to update any pending "webpage.Localise = Ignore/Localised" rows
                batch = await repo.GetWebPagesToLocaliseAsync(batchSize);       // get next batch
            }
            return success;
        }
    }
}
