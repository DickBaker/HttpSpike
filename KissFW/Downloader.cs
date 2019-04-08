using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace KissFW
{
    public class Downloader
    {
        private readonly IHttpParser Httpserver;
        private readonly IRepository Dataserver;
        private readonly string HtmlPath;               // subfolder to save *.html
        private readonly string OtherPath;              //  ditto for other extension types

#pragma warning disable GCop302 // Since '{0}' implements IDisposable, wrap it in a using() statement
        static readonly HttpClient Client = new HttpClient(
            new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
        { Timeout = new TimeSpan(0, 0, 10) };
#pragma warning restore GCop302 // Since '{0}' implements IDisposable, wrap it in a using() statement

        public Downloader(IHttpParser httpserver, IRepository dataserver, string htmlPath, string otherPath = null)
        {
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

        private bool DeleteLatterIfSame(string filespecA, string filespecB)
        {
            var hashOld = GetHash(filespecA);
            var hashNew = GetHash(filespecB);
            if (hashOld == hashNew)
            {
                File.Delete(filespecB);         // delete the new copy, but keep the old one (retain creation datetime)
                return true;
            }
            return false;
        }

        public async Task<bool> FetchFileAsync(WebPage webpage)
        {
            string ct2extn, filespec3;
            var url = Utils.TrimOrNull(webpage?.Url) ?? throw new InvalidOperationException("FetchFileAsync(webpage.Url) cannot be null");
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            using (var rsp = await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(continueOnCapturedContext: true))  // false
            {
                Console.WriteLine($"{rsp.StatusCode} {rsp.ReasonPhrase}");

                if (!rsp.IsSuccessStatusCode)
                {
                    webpage.NeedDownload = false;                                   // prevent any [infinite] retry loop
                    webpage.Filespec = $"~{rsp.StatusCode}({rsp.ReasonPhrase})";
                    await Dataserver.SaveChangesAsync();                            // do it NOW !
                                                                                    //await Task.FromResult(result: false);                           // on error ignore (no exception)
                    throw new ApplicationException($"web response {rsp.StatusCode}({rsp.ReasonPhrase}) for Url={webpage.Url}");
                    /*
                    https://tabletalkmagazine.com/wp-content/themes/launchframe/favicons/touch-icon-ipad-76x76.png
                    https://tabletalkmagazine.com/wp-content/themes/launchframe/favicons/touch-icon-ipad-retina-152x152.png
                    https://tabletalkmagazine.com/wp-content/themes/launchframe/favicons/touch-icon-iphone-60x60.png
                    https://tabletalkmagazine.com/wp-content/themes/launchframe/favicons/touch-icon-iphone-retina-120x120.png
                    https://d1nve9bnh93d5n.cloudfront.net/static/images/ligonier_touch.png

                    https://www.ligonier.org/about/who-we-are/Ligonier.org
                    https://www.ligonier.org/academics
                    https://www.ligonier.org/about/Ligonier.org
                    https://www.ligonier.org/account
                    http://reformationtrust.com/about
                    "http://reformationtrust.com/account/login"
                    */
                }
                //rsp.EnsureSuccessStatusCode();

                var mtyp = rsp.Content.Headers.ContentType.MediaType;
                ct2extn = MimeCollection.LookupExtnFromMime(mtyp);
                //if (!Utils.MimeToExtn.TryGetValue(mtyp, out ct2extn))           // see if contentType is known
                if (ct2extn == null)
                {
                    //  || !ct2extn.IsText TODO: write non-UTF-8 file code
                    // *** accept original extn as worst-case ?? ***
                    await Task.FromResult(result: false);                           // abort if no explicit content (i.e. ignore extn in caller's DraftFilespec)
                }
                var filenameOnly = Utils.TrimOrNull(Path.GetFileNameWithoutExtension(webpage.DraftFilespec));
                if (filenameOnly == null || filenameOnly == "unknown")
                {
                    filenameOnly = Path.GetRandomFileName();                        // NB this produces a file5678.ext4 format but real extn added 3 lines below
                }
                var folder = (ct2extn == "html") ? HtmlPath : OtherPath;            // device & folder path
                var filespec1 = filenameOnly + "." + ct2extn;                       // filename & extension (ignore any extn in DraftFilespec)
                var filespec2 = Path.Combine(folder, filespec1);                    // full spec of intended target
                var colliding = File.Exists(filespec2);                             // does target already pre-exist ?
                if (colliding)
                {
                    webpage.DraftFilespec = filespec1;                              // keep our 2nd choice of fn.extn [simple debug aid]
                    var filext = Path.GetRandomFileName() + "." + ct2extn;          // use alternate file target
                    filespec3 = Path.Combine(folder, filext);
                }
                else
                {
                    filespec3 = filespec2;                                              // final target spec (either as filespec2 or a generated file spec)
                }

                //var charset = rsp.Content.Headers.ContentType.CharSet;
                //var prms = rsp.Content.Headers.ContentType.Parameters;
                //var content = await rsp.Content.ReadAsStringAsync();

                try
                {
                    using (var strm = await rsp.Content.ReadAsStreamAsync().ConfigureAwait(continueOnCapturedContext: false))
                    using (var fs = File.Create(filespec3))
                    {
                        await strm.CopyToAsync(fs).ConfigureAwait(continueOnCapturedContext: false);
                        fs.Flush();
                    }

                    if (colliding)
                    {
                        // compare before & after files
                        if (DeleteLatterIfSame(filespec2, filespec3))               // compare files and delete latter if same
                        {
                            filespec3 = filespec2;                                  // revert to pre-existing filespec after delete
                        }
                    }
                    webpage.Filespec = filespec3;                                   // persist the ultimate filespec
                    Console.WriteLine($"{webpage.DraftFilespec}\t{filespec3}");
                }
                catch (Exception excp)
                {
                    Console.WriteLine(excp);    // e.g. "Cannot access a closed Stream." if debugging too slowly!
                    return false;
                }
            }       // termination of using req, rsp for Dispose()

            if (ct2extn == "html")
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
                                File.Move(filespec3, filespec4);
                                Console.WriteLine($"moved {filespec3} => {filespec4}");
                            }
                        }
                        catch (Exception excp)
                        {
                            Console.WriteLine($"moved {filespec3} => {filespec4} failed with {excp.Message}");
                        }
                    }
                }
                var links = Httpserver.GetLinks();                  // get all links (<a href> etc) and de-duplicate
                await Dataserver.AddLinksAsync(webpage, links);      // add to local repository
            }

            // flush changes to db (e.g. Filespec, and lotsa links if HTML)
            try
            {
                var rowcnt = await Dataserver.SaveChangesAsync();
                if (rowcnt > 0)
                {
                    Console.WriteLine($"{rowcnt} changes written");
                }
            }
            catch (Exception except)
            {
                Console.WriteLine(except.Message);      // swallow the error. NB may have tainted the EF changeset
            }
            return true;
        }

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

        private static void SetDefaultHeaders()
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
