using Akka.Actor;
using Akka.Event;
using DownloadExtractLib.Messages;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DownloadExtractLib
{
    public class ParseActor : ReceiveActor
    {
        const string SUBFOLDER = "_files";                   // relative folder where to download any dependent resources

        const string Backslash = @"\";                          // or Path.DirectorySeparatorChar.ToString()
        readonly ILoggingAdapter _Log = Context.GetLogger();
        Uri BaseUri;
        string FolderForHtml, FolderNonHtml;

        public ParseActor()
        {
            _Log.Info("ParseActor({0}) created", Self.Path);
            Receive<ParseHtmlMessage>(DoParse);
        }

        bool DoParse(ParseHtmlMessage msg)
        {
            var filespec = msg.Filespec.Trim();             // if null/empty the doc.Load method will abort so don't check here
            _Log.Info("ParseActor({0}).ParseHtmlMessage({1})) starting", Self.Path, filespec);

            #region HAP docs
            /*
            // From File
            var doc = new HtmlDocument();
            doc.Load(filePath);

            // From String
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // From Web
            var url = "http://html-agility-pack.net/";
            var web = new HtmlWeb();
            var doc = web.Load(url);
            */
            #endregion            var doc = new HtmlDocument();

            var doc = new HtmlDocument();
            try
            {
                doc.Load(filespec);                                 // non-async but small-beer [local file] compared to CPU-bound parsing
            }
            catch (Exception excp)
            {
                _Log.Error("ParseActor({0}).ParseHtmlMessage({1})) exception({2}))", Self.Path, filespec, excp);
                Sender.Tell(new ParsedHtmlMessage(filespec, null, exception: excp));        // probably wasn't an HTML file
                return true;                                        // show ActorSystem we handled message [expect next one immediately!]
            }

            var fi = new FileInfo(filespec);
            FolderForHtml = fi.DirectoryName + Backslash;           // download *.html files into same folder [simplify a.html->b.thml->a.html nav]
            FolderNonHtml = FolderForHtml + SUBFOLDER + Backslash;   // put files for all other extensions into subfolder [created by first DownloadMessage]

            // if null or relative, we will ignore any relative Url's that we discover
            if (string.IsNullOrWhiteSpace(msg.Url))
            {
                BaseUri = null;
            }
            else
            {
                BaseUri = new Uri(msg.Url.Trim().ToLower());
                if (!BaseUri.IsAbsoluteUri)
                {
                    BaseUri = null;                 // otherwise Uri(Uri baseUri, Uri relativeUri) will ArgumentOutOfRangeException
                }
            }
            // HTML5 Specifies the base URL for all relative URLs in the page [max=1]
            var defaultbase = doc.DocumentNode.SelectSingleNode("head/base[href]");     // any HREF ? (could be solely TARGET)
            if (defaultbase != null)
            {
                var baseurl = defaultbase.Attributes["href"].Value;
                if (!string.IsNullOrWhiteSpace(baseurl))
                {
                    BaseUri = new Uri(baseurl.Trim().ToLower());
                }
            }

            //var anodes = doc.DocumentNode.SelectNodes("//a[@href]").OrderBy(n => n.Attributes["href"].Value.ToLowerInvariant());
            IEnumerable<DownloadMessage> anchors = null;
            try
            {
                anchors = (doc.DocumentNode.SelectNodes("//a[@href]") ?? new HtmlNodeCollection(null))  // HAP returns null if nothing found
                    .Select(nod => CombineUriToString(nod.Attributes["href"].Value, nod.Attributes["download"]?.Value, DefaultExtn_A(nod)));       // file.asp or file.aspx -> file.html
            }
            catch (Exception excp1)
            {
                _Log.Error("failed during Anchors extract ({})", excp1.Message);
                anchors = anchors ?? new List<DownloadMessage>();
            }

            /*
            EXCLUSIONS
                action      <form>
                cite        <blockquote>, <del>, <ins>, <q>
                formaction	<button>, <input>
                href	    <a>, <area>, <base>, <link>
                media	    <a>, <area>, <link>, <source>, <style>
                muted	    <video>, <audio>
                src	        <audio>, <embed>, <iframe>, <img>, <input>, <script>, <source>, <track>, <video>
                srcset	<img>, <source>
                target	<a>, <area>, <base>, <form>
                type	<button>, <embed>, <input>, <link>, <menu>, <object>, <script>, <source>, <style>
            */
            IEnumerable<DownloadMessage> links = null;
            try
            {
                //var TEMPlinks = (doc.DocumentNode.SelectNodes("//link[@href]") ?? new HtmlNodeCollection(null))
                //   .Where(n => n.Attributes["rel"].Value != "dns - prefetch" &&    // ignore stuff in the <head/>
                //          n.Name != "form")                                        //  and don't go submit nuffin !
                //   .Select(nod => CombineUriToString(nod.Attributes["href"].Value, nod.Attributes["download"]?.Value, DefaultExtn_Link(nod))).ToList();

                links = (doc.DocumentNode.SelectNodes("//link[@href]") ?? new HtmlNodeCollection(null))
                    .Where(n => n.Attributes["rel"].Value != "dns - prefetch" &&    // ignore stuff in the <head/>
                           n.Name != "form")                                        //  and don't go submit nuffin !
                    .Select(nod => CombineUriToString(nod.Attributes["href"].Value, nod.Attributes["download"]?.Value, DefaultExtn_Link(nod)));
            }
            catch (Exception excp2)
            {
                _Log.Error("failed during Links extract ({})", excp2.Message);
                links = links ?? new List<DownloadMessage>();
            }

            IEnumerable<DownloadMessage> images = null;
            try
            {
                //var TEMPimages = (doc.DocumentNode.SelectNodes("//img[@src]") ?? new HtmlNodeCollection(null))
                //    .Select(nod => CombineUriToString(nod.Attributes["href"].Value, nod.Attributes["download"]?.Value, DefaultExtn_Img(nod))).ToList();
                images = (doc.DocumentNode.SelectNodes("//img[@src]") ?? new HtmlNodeCollection(null))
                    .Select(nod => CombineUriToString(nod.Attributes["href"].Value, nod.Attributes["download"]?.Value, DefaultExtn_Img(nod)));
            }
            catch (Exception excp2)
            {
                _Log.Error("failed during images extract ({})", excp2.Message);
                images = images ?? new List<DownloadMessage>();
            }
#if DEBUG
            foreach (var anchor in anchors)
            {
                Console.WriteLine($"Anchor {anchor?.Url} => {anchor?.TargetPath}");
            }
            foreach (var link in links)
            {
                Console.WriteLine($"Link  {link?.Url} => {link?.TargetPath}");
            }
            foreach (var image in images)
            {
                Console.WriteLine($"Img  {image?.Url} => {image?.TargetPath}");
            }
#endif
            var dlmsgs = anchors                    // distinct and sorted List<DownloadMessage>
                .Union(links)
                .Union(images)
                .Where(url => url != null)
                .ToList();

            Sender.Tell(new ParsedHtmlMessage(filespec, dlmsgs, msg.Url));

            return true;                                        // show ActorSystem we handled message [expect next one immediately!]
        }

        string DefaultExtn_A(HtmlNode node)
        {
            return (node.GetAttributeValue("fake", "html") == "html")
            ? ".html"
            : ".txt";
        }

        string DefaultExtn_Link(HtmlNode node)
        {
            if (node.Attributes["rel"]?.Value == "stylesheet")
            {
                return ".css";
            }
            if (node.Attributes["rel"]?.Value == "shortcut icon")
            {
                return ".ico";
            }
            return ".css";
        }

        string DefaultExtn_Img(HtmlNode node)
        {
            var imgtyp = node.GetAttributeValue("resp", "image");
            switch (imgtyp)
            {
                case "image/jpeg":
                    return ".jpeg";
                default:
                    return ".bin";        // unknown media type so hope for file extension or make spurious extension
            }
        }

        DownloadMessage CombineUriToString(
            string relativeOrAbsoluteUrl,       // caller has already forced to lc, so don't do again and use InvariantCulture
            string download = "",                 // default folder
            string extn = "")                   // default extension
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(relativeOrAbsoluteUrl) && !relativeOrAbsoluteUrl.StartsWith("javascript:", StringComparison.InvariantCulture))
                {
                    var tmpuri = (BaseUri == null)
                            ? new Uri(relativeOrAbsoluteUrl)
                            : new Uri(BaseUri, relativeOrAbsoluteUrl);
                    if (tmpuri.IsAbsoluteUri)
                    {
                        download = Path.GetFileName(download ?? "");          // just file.ext after stripping off any device:\folders evil
                        var fileName1 = Path.GetFileNameWithoutExtension(download);
                        var extn1 = Path.GetExtension(download);
                        var fs = tmpuri.Segments[tmpuri.Segments.Length - 1].Trim();        // "file.extn" or "file" or ".extn"
                        var fileName2 = Path.GetFileNameWithoutExtension(fs);
                        var extn2 = Path.GetExtension(fs);
                        var extn3 = Concatenate(extn1, extn2, extn);      // .extn derived from HTML attributes
                        if (extn3.EndsWith(".htm", StringComparison.InvariantCultureIgnoreCase))
                        {
                            extn3 += "l";                                             // migrate any .htm -> .html standard
                        }
                        var tgt =
                            ((extn3 == ".html") ? FolderForHtml : FolderNonHtml)    // folder
                            + Concatenate(fileName1, fileName2)                     // filename
                            + extn3;                                                // .extension
                        return new DownloadMessage(tmpuri.AbsoluteUri, tgt);
                    }
                    else
                    {
                        _Log.Info("CombineUriToString failed with ({0},{1})", BaseUri, relativeOrAbsoluteUrl);
                    }
                }
            }
            catch (Exception excp3)
            {
                _Log.Info("CombineUriToString crashed on ({0},{1}) with error {2}", BaseUri, relativeOrAbsoluteUrl, excp3);
                // throw;               // swallow error (if BaseUri==null && relativeOrAbsoluteUrl is relative
            }
            return null;                // entry will be culled when assigning newUrls (see ~30 lines above)
        }

        string Concatenate(params string[] xxx)
        {
            foreach (var item in xxx)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    return item.Trim();
                }
            }
            return null;
        }
    }
}