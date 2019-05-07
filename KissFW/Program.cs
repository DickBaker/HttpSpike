﻿using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using DownloadLib;
using HapLib;
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

            IAsyncPolicy AdoRetryPolicy =                               // TODO: probably should configure based on App.config
                Policy.Handle<Exception>(ex => true)                    // retry every exception! TODO: improve
                .WaitAndRetryAsync(5, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt) / 4));  // i.e. 0.5, 1, 2, 4, 8 second retries

            //IRepository repo = new Repository(dbctx);
            IRepository repo = new BulkRepository(dbctx, AdoRetryPolicy);

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

            var download = new Downloader(repo, Client, HttpRetryPolicy, HParser, htmldir, otherdir, backupdir);
            await DownloadAndParse(repo, batchSize, download);
            Console.WriteLine("*** DownloadAndParse FINISHED ***");

            var localise = new Localiser(repo, HParser, htmldir, backupdir);
            await HtmlLocalise(repo, batchSize, localise);
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
                    w => w.Url.Contains("rfc7230")                 // https://tools.ietf.org/html/rfc7230
                        || w.Url.Contains("rfc2616")              // https://www.w3.org/Protocols/rfc2616/rfc2616.html
                        || w.Url.Contains("w3schools.com/tags/ref_attributes.asp") // https://www.w3schools.com/tags/ref_attributes.asp
                    )
                .OrderBy(w => w.Url)
                .ToList();
            //var batch = dbctx.WebPages.Include("ConsumeFrom").Where(w => w.Url.StartsWith("http://amzn.to")).ToList();
            //var keywords = new int[] { 53954, 54180, 54194, 54196, 54197, 54311, 54312, 54313, 54339, 54747, 55782, 56309, 56549, 57214 };
            //batch = dbctx.WebPages
            //    .Include("ConsumeFrom")
            //    //.Include("SupplyTo")            // not necessary
            //    .Where(w => keywords.Contains(w.PageId))
            //    .OrderBy(w => w.Url)
            //    .ToList();
            batch = await repo.GetWebPagesToDownloadAsync(batchSize);      // get first batch (as List<WebPage>)
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
        static async Task HtmlLocalise(IRepository repo, int batchSize, Localiser localise)
        {
            var batch = await repo.GetWebPagesToLocaliseAsync(batchSize);       // get first batch (as List<WebPage>)
            while (batch.Count > 0)
            {
                foreach (var webpage in batch)                                  // iterate through [re-]obtained List
                {
                    var htmlFile = webpage.Filespec;
                    var backupFile = backupdir + Path.DirectorySeparatorChar + Path.GetFileName(htmlFile);

                    Console.WriteLine($"<<<{webpage.Url}\t~~>\t{htmlFile }>>>");
                    try
                    {
                        var changedLinks = localise.Translate(webpage);         // [sync] complete current page before starting the next
                        webpage.Localise = (changedLinks)
                            ? WebPage.LocaliseEnum.Localised                    // show Localise success
                            : WebPage.LocaliseEnum.Ignore;                      // pretend it wasn't wanted anyway
                    }
                    catch (Exception excp)                                      // either explicit from FetchFileAsync or HTTP timeout [TODO: Polly retries]
                    {
                        Console.WriteLine($"HtmlLocalise EXCEPTION\t{excp.Message}");   // see Filespec like '~%'
                        webpage.Localise = WebPage.LocaliseEnum.Ignore;                 // pretend it wasn't wanted anyway
                    }
                }
                var finalcnt = repo.SaveChanges();                              // flush to update any pending "webpage.Localise = Ignore/Localised" rows
                batch = await repo.GetWebPagesToLocaliseAsync(batchSize);       // get next batch
            }
        }
    }
}
