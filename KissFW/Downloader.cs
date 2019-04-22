﻿using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;
using Polly;
using Polly.Retry;

namespace KissFW
{
    public class Downloader
    {
        const string EXTN_SEPARATOR = ".";
        readonly IHttpParser Httpserver;
        readonly IRepository Dataserver;
        readonly string HtmlPath;               // subfolder to save *.html
        readonly string OtherPath;              //  ditto for other extension types

        //TODO: plug-in Polly as MessageProcessingHandler / whatever !
        readonly HttpClient Client;
        //= new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        //{ Timeout = new TimeSpan(0, 0, 10) };

        readonly IAsyncPolicy<HttpResponseMessage> _httpRetryPolicy;

        public Downloader(IRepository dataserver, HttpClient httpclient, IAsyncPolicy<HttpResponseMessage> policy, IHttpParser httpserver, string htmlPath, string otherPath = null)
        {
            Client = httpclient;
            _httpRetryPolicy = policy;
            Httpserver = httpserver;
            Dataserver = dataserver;
            HtmlPath = Utils.TrimOrNull(htmlPath) ?? throw new InvalidOperationException($"DownloadPage(htmlPath) cannot be null");
            OtherPath = Utils.TrimOrNull(otherPath) ?? HtmlPath;
            SetDefaultHeaders();
        }

        public string ByteToString(byte[] data)
        {
            var sBuilder = new StringBuilder();             // prepare to collect the bytes and create a string

            // Loop through each byte of the hashed data and format each one as a hexadecimal string.
            for (var i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }
            return sBuilder.ToString();     // Return the hexadecimal string
        }

        bool DeleteLatterIfSame(string filespecA, string filespecB)
        {
            var hashOld = GetHash(filespecA);
            var hashNew = GetHash(filespecB);
            var same = true;
            for (var i = 0; i < hashOld.Length; i++)
            {
                if (hashOld[i] != hashNew[i])
                {
                    same = false;
                    break;
                }
            }
            if (same)
            {
                File.Delete(filespecB);         // delete the new copy, but keep the old one (retain creation datetime)
                return true;
            }
            return false;
        }

        public async Task<bool> FetchFileAsync(WebPage webpage)
        {
            string extn = null, filespec3;
            var url = Utils.TrimOrNull(webpage?.Url) ?? throw new InvalidOperationException("FetchFileAsync(webpage.Url) cannot be null");
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            using (var rsp = await _httpRetryPolicy.ExecuteAsync(      // TODO: rewrite as fatal timeouts won't be caught here [caller will catch]
                () => Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead)))
            {
                Console.WriteLine($"{rsp.StatusCode} {rsp.ReasonPhrase}");

                if (!rsp.IsSuccessStatusCode)                                       // timeout will be thrown directly to caller [no catch here]
                {
                    //webpage.NeedDownload = false;                                 // prevent any [infinite] retry loop
                    webpage.Filespec = $"~{rsp.StatusCode}({rsp.ReasonPhrase})";
                    //await Dataserver.SaveChangesAsync();                          // do it NOW !
                    //await Task.FromResult(result: false);                         // on error ignore (no exception)
                    throw new ApplicationException($"web response {rsp.StatusCode}({rsp.ReasonPhrase}) for Url={webpage.Url}");
                }
                TargetFilespecs(webpage, rsp, out extn, out var filespec2, out filespec3);  // determine appropriate file target(s)

                //var charset = rsp.Content.Headers.ContentType.CharSet;
                //var prms = rsp.Content.Headers.ContentType.Parameters;
                //var content = await rsp.Content.ReadAsStringAsync();

                try
                {
                    using (var strm = await rsp.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false))
                    using (var fs = File.Create(filespec3))             // TODO: write non-UTF - 8 file code
                    {
                        await strm.CopyToAsync(fs).ConfigureAwait(continueOnCapturedContext: false);
                        fs.Flush();
                    }

                    if (!filespec2.Equals(filespec3, StringComparison.InvariantCultureIgnoreCase))
                    {
                        // compare before & after files
                        if (DeleteLatterIfSame(filespec2, filespec3))               // compare files and delete latter if same
                        {
                            filespec3 = filespec2;                                  // revert to pre-existing filespec after delete
                        }
                    }
                    webpage.Filespec = filespec3;                                   // persist the ultimate filespec [N.B. may change by Title later]
                    webpage.NeedDownload = false;                                   // download completed successfully
                    Console.WriteLine($"{webpage.DraftFilespec}\t{filespec3}");
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
            }       // termination of using req, rsp for Dispose()

            if (extn == "html")
            {
                await ExtractLinks(webpage, filespec3, url);
            }

            // flush changes to db (e.g. Filespec, and lotsa links if HTML)
            try
            {
                var rowcnt = await Dataserver.SaveChangesAsync();   // TODO: remove this sync perf-killer
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

        private void TargetFilespecs(WebPage webpage, HttpResponseMessage rsp, out string extn, out string filespec2, out string filespec3)
        {
            var filenameOnly = Utils.TrimOrNull(Path.GetFileNameWithoutExtension(webpage.DraftFilespec));
            var mtyp = rsp.Content.Headers.ContentType.MediaType;           // "application/json", "application/manifest+json"
            extn = MimeCollection.LookupExtnFromMime(mtyp)                  // MediaType takes priority over DraftFilespec for EXTN
                ?? Utils.TrimOrNull(Path.GetExtension(webpage.DraftFilespec));

            var contdisp = rsp.Content.Headers.ContentDisposition;
            if (contdisp != null)
            {
                var DispositionType = contdisp.DispositionType;             // "attachment"
                var FileName = contdisp.FileName;                           // "json.json"
                var FileNameStar = contdisp.FileNameStar;
                var CreationDate = contdisp.CreationDate;
                var ModificationDate = contdisp.ModificationDate;
                var ReadDate = contdisp.ReadDate;
                var Name = contdisp.Name;
                //FileName = contdisp.Parameters.Name == "filename" ? contdisp.Parameters[0].Value : null;
                if (DispositionType != null || FileName != null || FileNameStar != null || CreationDate != null || ModificationDate != null || ReadDate != null || Name != null)
                {
                    Console.WriteLine($"DispositionType={DispositionType}");
                }
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
                throw new ApplicationException($"unknown extn for Url={webpage.Url}");
            }
            if (filenameOnly == null)
            {
                filenameOnly = RandomFilenameOnly();                        // NB this produces a file5678.ext4 format but real extn added 3 lines below
            }
            var folder = (extn == "html") ? HtmlPath : OtherPath;           // device & folder path
            var filespec1 = filenameOnly + EXTN_SEPARATOR + extn;           // filename & extension (ignore any extn in DraftFilespec)
            filespec2 = filespec3 = (!string.IsNullOrWhiteSpace(webpage.Filespec))
                ? webpage.Filespec                                          // if this is a reload, assign the original to filespec2 (will compare later)
                : Path.Combine(folder, filespec1);
            if (File.Exists(filespec2) || filespec2.Length > WebPage.FILESIZE)
            {
                webpage.DraftFilespec = filespec1;                          // keep our 2nd choice of fn.extn [simple debug aid]
                var filext = RandomFilenameOnly() + EXTN_SEPARATOR + extn;  // use alternate file target [N.B. no 100% guarantee that file doesn't exist]
                filespec3 = Path.Combine(folder, filext);
                Debug.Assert(filespec3.Length <= WebPage.FILESIZE, "reduce folder length for htmldir / otherdir in App.config for AppSettings");
                if (File.Exists(filespec3))                                 // no 100% guarantee that file5678.html file doesn't exist
                {
                    File.Delete(filespec3);
                }
            }
        }

        private async Task ExtractLinks(WebPage webpage, string filespec3, string url)
        {
            Httpserver.LoadFromFile(url, filespec3);
            //await Httpserver.LoadFromWebAsync("http://stackoverflow.com/questions/2226554/c-class-for-decoding-quoted-printable-encoding", CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(Httpserver.Title))
            {
                var filespec4 = Path.Combine(HtmlPath, Utils.MakeValid(Httpserver.Title.Trim() + ".html"));     // intrinsic spec by page content
                if (filespec3 != filespec4)
                {
                    try
                    {
                        if (File.Exists(filespec4))
                        {
                            if (!DeleteLatterIfSame(filespec4, filespec3))
                            {
                                Console.WriteLine($"leaving new {filespec3} as different to existing {filespec4}");
                            }
                            else
                            {
                                File.Move(filespec3, filespec4);
                                Console.WriteLine($"after DELETE: moved {filespec3} => {filespec4}");
                                webpage.Filespec = filespec4;                               // update to intrinsic filespec
                            }
                        }
                        else
                        {
                            var len3 = filespec3.Length - HtmlPath.Length;
                            var len4 = filespec4.Length - HtmlPath.Length;
                            var swapYN = (len3 < 15 || len3 > 50) && (len4 > 12 && len4 < 50);
                            if (swapYN)
                            {
                                File.Move(filespec3, filespec4);
                                Console.WriteLine($"moved {filespec3} => {filespec4}");
                            }
                            else
                            {
                                Console.WriteLine($"NOT moved {filespec3} => {filespec4}");
                            }
                        }
                    }
                    catch (Exception excp)
                    {
                        Console.WriteLine($"moving {filespec3} => {filespec4} failed with {excp.Message}");
                    }
                }
            }
            var links = Httpserver.GetLinks();                  // get all links (<a href> etc) and de-duplicate
            await Dataserver.AddLinksAsync(webpage, links);      // add to local repository
        }

        private static string RandomFilenameOnly() =>
            Path.GetFileNameWithoutExtension(                                   // this produces a file5678 format
                Path.GetRandomFileName());                                      //  from original file5678.ext4 format

        public byte[] GetHash(string filespec2)
        {
            byte[] mash;
            // compute MD5 for each of the pre-existing & new files and see if they match
            using (var myhash = MD5.Create())
            using (var fileStream = new FileStream(filespec2, FileMode.Open) { Position = 0 })       // Be sure fileStream is positioned at the beginning
            {
                mash = myhash.ComputeHash(fileStream);   // Compute the hash of the fileStream
            }
            return mash;
        }

        void SetDefaultHeaders()
        {
            Client.DefaultRequestHeaders.Accept.Clear();
            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/http"));
            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml"));
            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp", 0.8));
            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png", 0.8));
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/apng", 0.4));   // strange Chrome type ??
            //Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*", 0.1));          // don't ask what we can't digest (e.g. .gz)

            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            Client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
            Client.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
        }
    }
}
