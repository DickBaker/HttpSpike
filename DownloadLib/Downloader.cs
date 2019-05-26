using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;
using Polly;
using static Infrastructure.Models.WebPage;

namespace DownloadLib
{
    public class Downloader : IDownloader
    {
        const string EXTN_SEPARATOR = ".", HTML = "html", ERRTAG = "~";
        const int buffSize = 81920;             // default used by Stream.CopyToAsync
        readonly DateTime earliest = new DateTime(2000, 1, 1);
        readonly IHttpParser Httpserver;
        readonly IRepository Dataserver;
        readonly string HtmlPath;               // subfolder to save *.html
        readonly string OtherPath;              //  ditto for other extension types
        readonly string BackupPath;             //  ditto as backup
        readonly long MaxFileSize;              // don't download files bigger than 10 MB (default)
        readonly long MinReport = 500_000;      // don't raise IProgress.Report(percent) unless download at least this big

        //TODO: plug-in Polly as MessageProcessingHandler / whatever !
        readonly HttpClient Client;
        //= new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        //{ Timeout = new TimeSpan(0, 0, 10) };

        readonly IAsyncPolicy<HttpResponseMessage> _httpRetryPolicy;

        public Downloader(IRepository dataserver, HttpClient httpclient, IAsyncPolicy<HttpResponseMessage> policy, IHttpParser httpserver,
            string htmlPath, string otherPath = null, string backupPath = null,
            long maxfilesize = 10_000_000)
        {
            Client = httpclient;
            _httpRetryPolicy = policy;
            Httpserver = httpserver;
            Dataserver = dataserver;
            HtmlPath = Utils.TrimOrNull(htmlPath) ?? throw new InvalidOperationException($"DownloadPage(htmlPath) cannot be null");
            OtherPath = Utils.TrimOrNull(otherPath) ?? HtmlPath;
            BackupPath = backupPath;
            MaxFileSize = maxfilesize;
            SetDefaultHeaders();
        }

        /*
        string ByteToString(byte[] data)
        {
            var sBuilder = new StringBuilder();             // prepare to collect the bytes and create a string

            // Loop through each byte of the hashed data and format each one as a hexadecimal string.
            for (var i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }
            return sBuilder.ToString();     // Return the hexadecimal string
        }
        */

        bool CompareFiles(string filespecA, string filespecB)
        {
            if (filespecA.Equals(filespecB, StringComparison.InvariantCultureIgnoreCase))
            {
                return false;                           // filespecs are identical, so caller must take NO ACTION on IT
            }
            var hashOld = GetHash(filespecA);
            var hashNew = GetHash(filespecB);
            for (var i = 0; i < hashOld.Length; i++)
            {
                if (hashOld[i] != hashNew[i])
                {
                    return false;                       // contents differ, files as-was
                }
            }
            return true;                                // files' content identical and delete succeeded
        }

        /// <summary>
        ///     compare contents of filespecA, filespecB and if same delete filespecB
        /// </summary>
        /// <param name="filespecA">first filespec</param>
        /// <param name="filespecB">second filespec</param>
        /// <returns>true=files identical and filespecB deleted, else false with files as-was</returns>
        bool DeleteLatterIfSame(string filespecA, string filespecB)
        {
            if (CompareFiles(filespecA, filespecB))
            {
                try
                {
                    File.Delete(filespecB);                 // delete the new copy, but keep the old one (retain creation datetime)
                    return true;                            // files' content identical and delete succeeded
                }
                catch (Exception excp)
                {
                    Console.WriteLine($"DeleteLatterIfSame: error deleting {filespecB}\t{excp.Message}");   // warn but continue
                }
            }
            return false;                           // either same ONE file (filespecA=filespecB)
                                                    //  or the TWO files' content differ
                                                    //   both files as-was but caller should use filespecA
        }

        async Task ExtractLinks(WebPage webpage)
        {
            var filespec3 = webpage.Filespec;
            var prevParents = webpage.ConsumeFrom.Select(w => w.Url).ToList();
            Httpserver.LoadFromFile(webpage.Url, filespec3);
            //await Httpserver.LoadFromWebAsync("http://stackoverflow.com/questions/2226554/c-class-for-decoding-quoted-printable-encoding", CancellationToken.None);
            var filespec4 = Utils.TrimOrNull(Httpserver.Title);
            if (filespec4 != null)
            {
                filespec4 = Path.Combine(HtmlPath, Utils.MakeValid(filespec4 + ".html"));     // intrinsic spec by page content
                if (!filespec3.Equals(filespec4, StringComparison.InvariantCultureIgnoreCase) && filespec4.Length <= FILESIZE)
                {
                    try
                    {
                        if (File.Exists(filespec4))
                        {
                            if (DeleteLatterIfSame(filespec4, filespec3))
                            {
                                Console.WriteLine($"after DELETE: moved for {webpage.Url}\n  deleted\t{filespec3}\n  reused\t{filespec4}");
                                webpage.Filespec = filespec4;                               // update row to use previous file
                            }
                            else
                            {
                                Console.WriteLine($"leaving as different\n\twas:\t{filespec4}\n\tnow:\t{filespec3}");
                            }
                        }
                        else
                        {
                            var len3 = filespec3.Length - HtmlPath.Length;
                            var len4 = filespec4.Length - HtmlPath.Length;
                            var swapYN = (len3 < 15 || len3 > 60) || (len4 > 12 && len4 < 120);
                            if (swapYN)
                            {
                                File.Move(filespec3, filespec4);
                                webpage.Filespec = filespec4;
                                Console.WriteLine($"moved\t{filespec3}\n  =>\t{filespec4}");
                            }
                            else
                            {
                                Console.WriteLine($"NOT moved\t{filespec3}\n  =>\t\t{filespec4}");
                            }
                        }
                    }
                    catch (Exception excp)
                    {
                        Console.WriteLine($"moving {filespec3} => {filespec4} failed with\n{excp.Message}");
                    }
                }
            }
            var links = Httpserver.GetLinks();                  // get all links (<a href> etc) and de-duplicate
            var oldies = prevParents.Except(links.Keys, StringComparer.InvariantCultureIgnoreCase).ToList();
            if (oldies?.Count > 0)
            {
                var consumes = webpage.ConsumeFrom.Where(w => oldies.Contains(w.Url)).ToArray();    // unvarying list
                foreach (var oldie in consumes)
                {
                    var killed = webpage.ConsumeFrom.Remove(oldie);         // varying list (so can't be foreach iterable)
                    if (killed)
                    {
                        Console.WriteLine($"\tremoved [{oldie.PageId}]:\t{oldie.Url}");
                    }
                    else
                    {
                        Console.WriteLine($"\tremoval failed [{oldie.PageId}]:\t{oldie.Url}");
                    }
                }
            }

            await Dataserver.AddLinksAsync(webpage, links);     // add to local repository
        }

        public Task<bool> FetchFileAsync(WebPage webpage, IProgress<int> progress = null) => FetchFileAsync(webpage, CancellationToken.None, progress);
        public async Task<bool> FetchFileAsync(
            WebPage webpage,
            CancellationToken ct,
            IProgress<int> progress = null)
        {
            string extn = null, filespec3, location, draft = null;
            var akaUrls = new List<string>();

            var url = Utils.TrimOrNull(webpage?.Url) ?? throw new InvalidOperationException("FetchFileAsync(webpage.Url) cannot be null");

            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            using (var rsp = await _httpRetryPolicy.ExecuteAsync(                   // TODO: rewrite as fatal timeouts won't be caught here [caller will catch]
                (ct2) => Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct2), ct))
            {                                                                       // this block serves as Dispose() context for req and rsp
                Console.WriteLine($"{(int)rsp.StatusCode} {rsp.ReasonPhrase}");

                if (!rsp.IsSuccessStatusCode)                                       // timeout will be thrown directly to caller [no catch here]
                {
                    //webpage.NeedDownload = false;                                 // prevent any [infinite] retry loop
                    webpage.Filespec = $"{ERRTAG}{rsp.StatusCode}({rsp.ReasonPhrase})";
                    if (!(new int[] { 404, 303 }).Contains((int)rsp.StatusCode))
                    {
                        Console.WriteLine($"unexpected error{(int)rsp.StatusCode}");
                    }
                    rsp.EnsureSuccessStatusCode();                                  // raise official exception
                }
                var filesize = rsp.Content?.Headers?.ContentLength ?? 0;            // 249044384
                if (filesize > MaxFileSize)
                {
                    webpage.Filespec = $"~FileSize({filesize}) too big({MaxFileSize})";
                    webpage.Download = DownloadEnum.Ignore;                         // forget it (caller must persist change)
                    return false;
                }
                TargetFilespecs(webpage, rsp, out extn, out var filespec2, out filespec3);  // determine appropriate file target(s)

                DateTimeOffset? CreationDate = null, ModificationDate = null;
                //var ctyp = rsp.Content?.Headers?.ContentType.MediaType;           // "application/octet-stream"
                //var charset = rsp.Content.Headers.ContentType.CharSet;
                //var prms = rsp.Content.Headers.ContentType.Parameters;
                var contdisp = rsp.Content.Headers?.ContentDisposition;
                if (contdisp != null)
                {
                    var DispositionType = contdisp.DispositionType;                 // "attachment" (separate from main body of HTTP response) or "inline" (display automatically)
                    var FileNameStar = contdisp.FileNameStar;
                    var Name = contdisp.Name;
                    var FileName = Path.GetFileName(Utils.MakeValid(contdisp.FileName ?? FileNameStar ?? Name));    // "json.json" (prevent any malicious device/folder spec)
                    var ReadDate = contdisp.ReadDate;
                    CreationDate = contdisp.CreationDate ?? ReadDate;
                    ModificationDate = contdisp.ModificationDate ?? rsp.Content.Headers.LastModified ?? CreationDate;
                    if (FileNameStar != null || CreationDate != null || ModificationDate != null || ReadDate != null || Name != null)
                    {
                        Console.WriteLine($"DispositionType={DispositionType}, FileName={FileName}, CreationDate={CreationDate}, ModificationDate={ModificationDate}, ReadDate={ReadDate}, Name={Name}");
                    }
                }

                try
                {
                    using (var readStream = await rsp.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false))
                    {
                        using (var writeStream = File.Create(filespec3))             // TODO: write non-UTF - 8 file code
                        {
                            if (progress == null || filesize <= 0 || filesize < MinReport)
                            {
                                await readStream.CopyToAsync(writeStream, buffSize, ct).ConfigureAwait(continueOnCapturedContext: false);

                            }
                            else
                            {
                                await StreamCopyWithProgressAsync(readStream, writeStream, filesize, progress, ct);

                            }
                            writeStream.Flush();        // Clears buffers for this stream and causes any buffered data to be written to the file
                        }
                    }

                    // now all disk & network I/O completed [but using (var rsp) still undisposed] ..
                    // .. see how new downloaded file compares with any previous, then set LastModDate if declared
                    if (!filespec2.Equals(filespec3, StringComparison.InvariantCultureIgnoreCase))
                    {
                        // compare before & after files
                        if (DeleteLatterIfSame(filespec2, filespec3))               // compare files and delete latter if same
                        {
                            filespec3 = filespec2;                                  // revert to pre-existing filespec after delete
                        }
                        else
                        {
                            var dt = CreationDate?.UtcDateTime;                     // rsp.Content.Headers.ContentDisposition.CreationDate
                            if (dt != null && earliest <= dt.Value && dt.Value <= DateTime.UtcNow)
                            {
                                File.SetCreationTimeUtc(filespec3, dt.Value);       // "set date and time the file was created"
                            }
                            dt = ModificationDate?.UtcDateTime;                     // rsp.Content?.Headers?.LastModified ?? rsp.Content.Headers.LastModified
                            if (dt != null && earliest <= dt.Value && dt.Value <= DateTime.UtcNow)
                            {
                                File.SetLastWriteTimeUtc(filespec3, dt.Value);      // "set date and time last written to"
                            }
                        }
                    }
                    webpage.Filespec = filespec3;                                   // persist the ultimate filespec [N.B. may change by Title later]
                    webpage.Download = DownloadEnum.Downloaded;                     // download completed successfully
                    Console.WriteLine($"{webpage.DraftFilespec}\t{filespec3}");

                    location = rsp.Headers?.Location?.AbsoluteUri;
                    if (!string.IsNullOrWhiteSpace(location) && !webpage.Url.Equals(location, StringComparison.InvariantCultureIgnoreCase))
                    {
                        akaUrls.Add(location);
                    }
                    else
                    {
                        if (rsp.Headers.TryGetValues("location", out var locs))
                        {
                            Console.WriteLine("DEBUG: check Location header");
                        }
                    }
                    location = rsp.RequestMessage.RequestUri.AbsoluteUri;      // Utils.NoTrailSlash(
                    if (!akaUrls.Contains(location) && !webpage.Url.Equals(location, StringComparison.InvariantCultureIgnoreCase))
                    {
                        akaUrls.Add(location);
                    }

                }
                catch (Exception excp)
                {
                    Console.WriteLine($"FetchFileAsync EXCEPTION {excp}");          // e.g. "Cannot access a closed Stream." if debugging too slowly!
                    /*
                    "C:\\Ligonier\\webcache\\assets\\Ligonier Ministries on Twitter &quot;The only way hearts are going to change, lives are going to change, healing for sins of the past, is through the gospel of Jesus Christ that heals hearts and homes and families and relationships and nations. â€”@BurkParsons https t.co Z6zr5kBkvK #ligcon&quot;.json"
                    The specified path, file name, or both are too long. The fully qualified file name must be less than 260 characters, and the directory name must be less than 248 characters.
                    */
                    return false;
                }
            }       // termination of using req, rsp so now both had Dispose() invoked

            // process any sneaky redirections that happened under the covers
            if (akaUrls.Count > 1)
            {
                Console.WriteLine("investigate multiple aliasses");
            }
            draft = Path.GetFileName(filespec3);
            foreach (var redurl in akaUrls)
            {
                if (url == redurl)
                {
                    continue;               // if same then no redirect occured
                }
                var redpage = await Dataserver.GetWebPageByUrlAsync(redurl);    // Utils.NoTrailSlash()
                if (redpage == null)
                {
                    redpage = Dataserver.AddWebPage(new WebPage(redurl, draft, filespec3, DownloadEnum.Downloaded,
                        webpage.Localise == LocaliseEnum.Ignore ? LocaliseEnum.Ignore : LocaliseEnum.ToLocalise));  // propagate any localising
                    //await Dataserver.SaveChangesAsync();                        // this MUST have been persisted before p_ActionWebPage [no longer true cf. SaveLinks]
                }
                else
                {
                    if (redpage.DraftFilespec != draft
                        || redpage.Filespec != filespec3
                        || redpage.Localise != webpage.Localise)
                    {
                        redpage.DraftFilespec = redpage.DraftFilespec ?? draft;
                        if (string.IsNullOrWhiteSpace(redpage.Filespec))
                        {
                            redpage.Filespec = filespec3;
                        }
                        else
                        {
                            if (!redpage.Filespec.Equals(filespec3, StringComparison.InvariantCultureIgnoreCase))
                            {
                                var backupfs = BackupPath == null ? null : Path.Combine(BackupPath, Path.GetFileName(redpage.Filespec));
                                Utils.RetireFile(redpage.Filespec, backupfs, filespec3);
                            }
                        }
                        if (redpage.Localise == LocaliseEnum.Ignore && webpage.Localise != LocaliseEnum.Ignore)
                        {
                            redpage.Localise = LocaliseEnum.ToLocalise;     // must localise the page just [re-]downloaded
                        }
                    }
                    redpage.Download = DownloadEnum.Downloaded;
                }

                // remove any dependencies from the original page to leave ONE entry from old to redirect page
                //  Leave redirected page as-is, and p_ActionWebPage sproc will upsert in Sql db
                foreach (var dad in webpage.ConsumeFrom.ToArray())
                {
                    if (dad.Url != redurl)
                    {
                        if (!redpage.ConsumeFrom.Contains(dad))
                        {
                            redpage.ConsumeFrom.Add(dad);                   // add link to the [probably new] redirect page (p_ActionWebPage sproc will run afterwards)
                        }
                        webpage.ConsumeFrom.Remove(dad);                    // remove any historic links to other pages from the old page
                    }
                }

                if (!webpage.ConsumeFrom.Contains(redpage))
                {
                    webpage.ConsumeFrom.Add(redpage);
                }
                webpage.Filespec = null;                        // file just downloaded now belongs to redirected page
                webpage.Download = DownloadEnum.Redirected;
                webpage.Localise = LocaliseEnum.Ignore;
                webpage = redpage;                              // ditto parsed links belong to the redirected page                
                Httpserver.BaseAddress = new Uri(redurl);       // re-base for relative links
                break;                                          // ignore any subsequent redirection candidates
            }

            if (extn == HTML)
            {
                await ExtractLinks(webpage);
            }

            // flush changes to db (e.g. Filespec, and lotsa links if HTML)
            try
            {
                var rowcnt = await Dataserver.SaveChangesAsync();   // persist both EF and Ado (if need to call p_ActionWebPage sproc)
                if (rowcnt > 0)
                {
                    Console.WriteLine($"{rowcnt} changes written");
                }
                else
                {
                    Console.WriteLine($"FetchFileAsync({webpage.Url}) found nothing to write");    // should at least write NeedDownload=0 !
                }
            }
            catch (Exception except)
            {
                Console.WriteLine($"FetchFileAsync3: {except.Message}");        // swallow the error. NB may have tainted the EF changeset
            }
            return true;
        }

        async Task StreamCopyWithProgressAsync(Stream readStream, Stream writeStream, long filesize, IProgress<int> progress, CancellationToken ct)
        {
            var buff = new byte[buffSize];
            long ReportAbove;
            var reportInterval = ReportAbove = (MaxFileSize / 100 < buffSize) ? buffSize : MaxFileSize / 100;
            progress.Report(0);
            long SizeDone = 0;
            Console.WriteLine($"stream expecting {filesize}, interval={reportInterval}");
            do
            {
                ct.ThrowIfCancellationRequested();
                var readCount = await readStream.ReadAsync(buff, 0, buffSize);
                //Console.WriteLine($"streamIN({readCount})");
                if (readCount <= 0)
                {
                    break;
                }
                await writeStream.WriteAsync(buff, 0, readCount);
                SizeDone += readCount;
                if (SizeDone > ReportAbove && SizeDone != filesize)         // at interval but not at 100% (final one done below)
                {
                    progress.Report((int)(SizeDone * 100 / filesize));
                    ReportAbove += reportInterval;
                }
            } while (SizeDone < MaxFileSize);
            progress.Report(100);                                    // report the final 100%
        }

        byte[] GetHash(string filespec2)
        {
            byte[] mash;
            // compute MD5 for each of the pre-existing & new files and see if they match
            using (var myhash = MD5.Create())
            using (var fileStream = new FileStream(filespec2, FileMode.Open) { Position = 0 })  // Be sure fileStream is positioned at the beginning
            {
                mash = myhash.ComputeHash(fileStream);                      // Compute the hash of the fileStream
            }
            return mash;
        }

        void SetDefaultHeaders()        // Chrome does: text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3
        {
            Client.DefaultRequestHeaders.Accept.Clear();
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/http"));
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/jpeg"));     // whoops, forgot this!
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp", 0.8));
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png", 0.8));
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/apng", 0.4));   // strange Chrome type ??
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));          // don't ask what we can't digest (e.g. .gz)

            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.9));          // EVERYTHING !

            Client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            Client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        }

        /// <summary>
        ///     given HttpResponseMessage, invent two candidate target filespecs
        /// </summary>
        /// <param name="webpage">
        ///     current webpage being downloaded
        /// </param>
        /// <param name="rsp">
        ///     HttpResponseMessage
        /// </param>
        /// <param name="extn">
        ///     extension without the dot (e.g. "html")
        /// </param>
        /// <param name="filespec2">
        ///     filespec supposed from basic data before the HttpRequest
        /// </param>
        /// <param name="filespec3">
        ///     filespec based on the HttpResponse content (or generated if borderline case)
        /// </param>
        /// <remarks>
        ///     because this is executed DURING HttpResponse processing, it must be quick (no long debugging!) to avoid timeout
        /// </remarks>
        void TargetFilespecs(WebPage webpage, HttpResponseMessage rsp, out string extn, out string filespec2, out string filespec3)
        {
            var filenameOnly = Utils.TrimOrNull(Path.GetFileNameWithoutExtension(webpage.DraftFilespec));
            var mtyp = rsp.Content.Headers.ContentType.MediaType;           // "application/json", "application/manifest+json"
            extn = MimeCollection.LookupExtnFromMime(mtyp)                  // MediaType takes priority over DraftFilespec for EXTN
                ?? Utils.TrimOrNull(Path.GetExtension(webpage.DraftFilespec));

            var contdisp = rsp.Content.Headers.ContentDisposition;
            if (contdisp != null)
            {
                var FileName = Path.GetFileName(                            // e.g. "json.json" (prevent any malicious device/folder spec)
                    Utils.MakeValid(contdisp.FileName ?? contdisp.FileNameStar ?? contdisp.Name));  // filter out any spurious chars(e.g. double-quotes)
                if (FileName != null)
                {
                    string extn2;
                    (filenameOnly, extn2) = Utils.FileExtSplit(FileName);   // ContentDisposition.FileName takes priority over webpage.DraftFilespec for file NAME
                    if (!string.IsNullOrWhiteSpace(extn2))
                    {
                        extn = extn2;                                       // ContentDisposition takes priority over MediaType for EXTN
                    }
                }
            }

            if (extn == null)                                               // abort if no explicit content (i.e. ignore extn in caller's DraftFilespec)
            {
                //  || !ct2extn.IsText TODO: write non-UTF-8 file code
                /*
                "application/manifest+json"
                */
                throw new ApplicationException($"unknown extn for Url={webpage.Url}");  // TODO: consider accepting a plain filename (no extn)
            }
            var filespec1 = (filenameOnly ?? Utils.RandomFilenameOnly())    // NB this produces a file5678 format
                + EXTN_SEPARATOR + extn;                                    // filename & extension (ignore any extn in DraftFilespec)
            var folder = (extn == HTML) ? HtmlPath : OtherPath;             // device & folder path
            filespec2 = Utils.TrimOrNull(webpage.Filespec);                 // if this is a reload, assign the original to filespec2 (will compare later)
            filespec2 = filespec3 = (filespec2 != null && !filespec2.StartsWith(ERRTAG))   // skip any previous error message
                ? filespec2
                : Path.Combine(folder, filespec1);
            if (File.Exists(filespec2) || filespec2.Length > FILESIZE)
            {
                webpage.DraftFilespec = filespec1;                          // keep our 2nd choice of fn.extn [simple debug aid]
                do                                                          // use alternate file target
                {
                    filespec3 = Path.Combine(folder, Utils.RandomFilenameOnly() + EXTN_SEPARATOR + extn);   // no 100% guarantee that file5678.extn file doesn't exist
                    Debug.Assert(filespec3.Length <= FILESIZE, "reduce folder length for htmldir / otherdir in App.config for AppSettings");
                } while (File.Exists(filespec3));                           // hopefully rare and finite case !
            }
        }
    }
}
