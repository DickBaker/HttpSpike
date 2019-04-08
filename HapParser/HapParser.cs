using System;
using System.Collections.Generic;
using System.Net;
using HtmlAgilityPack;
using Infrastructure;
using Infrastructure.Interfaces;

namespace HapLib
{
    public class HapParser : IHttpParser
    {
        static readonly char[] CRLF = { '\r', '\n' };
        static readonly string[] LinkAttList = new string[] { "cite", "data", "href", "src", "srcset" };

        public HtmlDocument HtmlDoc { get; set; }

        const UriComponents MatchBits = UriComponents.HttpRequestUrl;   // flags denoting significant components for equality
                                                                        // i.e. Scheme, Host, Port, LocalPath, and Query data but NOT UserInfo & Fragment
        public Uri BaseAddress { get; set; }
        Uri reqUri;

        public Uri ReqUri
        {
            get => reqUri;
            set => reqUri = BaseAddress = string.IsNullOrEmpty(value.Fragment)
                    ? value
                    : Utils.NoFragment(value.AbsoluteUri);
        }

        public string Title { get; set; }

        public IDictionary<string, string> GetLinks()
        {
            //var Links = new Dictionary<string, string>(StringEquals);
            IDictionary<string, string> Links = new SortedDictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
            //Links = new SortedList<string, string>(StringComparer.InvariantCultureIgnoreCase);
            foreach (var att in LinkAttList)
            {
                FindLinks(att, Links);
            }
            return Links;
        }

        void FindLinks(string att, IDictionary<string, string> Links)
        {
            var ReqUriMatch = Utils.NoTrailSlash(ReqUri.GetComponents(MatchBits, UriFormat.SafeUnescaped));     // ignore Scheme|UserInfo|Fragment TODO: passes thru Scheme!
            foreach (var href in HtmlDoc.DocumentNode.SelectNodes($"//*[@{att}]"))
            {
                var url = href.Attributes[att].Value;
#if DEBUG
                if (url.StartsWith("//"))
                {
                    Console.WriteLine("observe relative scheme!");
                }
#endif
                // attempt extracting the [next] url, but ignore any exceptions
                try
                {
                    var uri = new Uri(BaseAddress, url);                        // this handles relative scheme e.g.
                                                                                //  "//www.slideshare.net/jeremylikness/herding-cattle-with-azure-container-service-acs"
                    if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)         // ignore "mailto" "javascript" etc
                        || href.Attributes["rel"]?.Value == "nofollow")
                    {
                        continue;
                    }

                    url = Utils.NoTrailSlash(uri.GetComponents(MatchBits, UriFormat.SafeUnescaped));     // ignore Scheme|UserInfo|Fragment TODO: passes thru Scheme!
                    if (url.Equals(ReqUriMatch, StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;                                               // skip any self-refs (e.g. with fragment) or as rel=canonical
                    }

                    (var filename, var extn) = Utils.FileExtLastSegment(uri.Segments[uri.Segments.Length - 1]);
                    filename = href.GetAttributeValue("download", href.GetAttributeValue("title", filename));
                    if (string.IsNullOrEmpty(filename))
                    {
                        foreach (var child in href.ChildNodes)
                        {
                            var titel = Utils.TrimOrNull(child.GetAttributeValue("title", null)) ??
                                Utils.TrimOrNull(child.InnerText.Replace('\r', ' ').Replace('\n', ' '));
                            if (titel != null)
                            {
                                filename = WebUtility.HtmlDecode(titel);
                                break;
                            }
                        }
                    }
                    if (string.IsNullOrEmpty(filename))
                    {
                        Console.WriteLine($"no filename({filename})");
                        filename = "unknown";
                    }
                    if (string.IsNullOrWhiteSpace(extn))        // #1 if explicit extn given then we use it
                    {
                        // #2 explicit "type" ?
                        var atribType = href.Attributes["type"]?.Value;
                        if (atribType != null)
                        {
                            //extn = Utils.LookupExtnFromMime(atribType);
                            extn = Infrastructure.Models.MimeCollection.LookupExtnFromMime(atribType);
                        }
                        // #3 <tag att='template'> lookup ?
                        if (extn == null)
                        {
                            extn = TagAttributeLookup(href, href.Name + "-" + att);
                        }
                    }
                    if (extn == null)
                    {
                        Console.WriteLine($"no extn({extn}) available");
                    }
                    else
                    {
                        filename += "." + extn;
                    }

                    filename = Utils.MakeValid(filename);       // remove any spurious chars
                    if (!Links.ContainsKey(url))                // ContainsKey does not have ignoreCase but Repository.PutWebPage does
                    {
                        Links[url] = filename;                  // add new url plus probable extn
                    }
                    else
                    {
                        if (Links[url] != filename)             // existing url: check matches same extn
                        {
                            if (!string.IsNullOrWhiteSpace(Links[url]))
                            {
                                Console.WriteLine($"variant file.ext for {url} : {Links[url]} {filename}");
                            }
                            Links[url] = filename;
                        }
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("failure extracting single url, continuing anyway");
                }
            }
        }

        public void SaveFile(string filespec) => HtmlDoc.Save(filespec);

        static string TagAttributeLookup(HtmlNode href, string tagatt)
        {
            switch (tagatt)
            {
                case "object-data":
                case "track-src":
                    return "data";

                case "a-href":
                case "area-href":
                case "base-href":
                case "blockquote-cite":
                case "del-cite":
                case "div-href":            // undoc
                case "embed-src":
                case "iframe-src":
                case "img-src":
                case "input-src":
                case "ins-cite":
                case "q-cite":
                case "source-src":
                case "source-srcset":
                case "span-href":           // undoc !!
                    return "html";

                case "script-src":
                    return "js";

                case "audio-src":
                case "video-src":
                    return "mp4";

                case "link-href":
                    switch (href.Attributes["rel"]?.Value)
                    {
                        case "canonical":           // w3schools doesn't doc this
                        case "next":
                        case "prev":
                            return "html";          // more of the same
                        case "apple-touch-icon":
                        case "apple-touch-icon-precomposed":
                        case "apple-touch-startup-image":
                        //case "shortcut icon":
                        //    return "png";         // check type='image/png'
                        case "icon":
                            return "ico";           // check type='image/x-icon'
                        case "stylesheet":          // check type='text/css'
                            return "css";
                        case "alternate":           // we already tried type so assume nothing
                        case "author":
                        case "dns-prefetch":
                        case "help":
                        case "license":
                        case "pingback":
                        case "preconnect":
                        case "prefetch":
                        case "preload":
                            return null;        // can't tell in advance what resource might be
                        case "search":
                            return "xml";
                        default:
                            // The mask-icon keyword is a registered extension to the predefined set of link types, but user agents are not required to support it in any way.
                            // <link rel="alternate" hreflang="x-default" href="https://twitter.com/jeremylikness">
                            Console.WriteLine($"unexpected link-href {href.OuterHtml}");
                            // next
                            break;
                    }
                    break;

                default:
                    Console.WriteLine($"unexpected tag/att pair {tagatt} in {href.OuterHtml}");     // TODO: make continue to ignore unknowns ??
                    break;
            }

            return null;
        }

        public void LoadFromFile(string url, string path)
        {
            LoadDoc(url, path);                 // load file into HtmlDoc, and set ReqUri, BaseAddress
            // extract title if any
            var TitleNode = HtmlDoc.DocumentNode.SelectSingleNode("//head/title");
            if (TitleNode != null)
            {
                Title = WebUtility.HtmlDecode(TitleNode.InnerText).Trim();      // removes leading CRLF
                var dlim = Title.IndexOfAny(CRLF);                              // so any embedded CRLF will be within revised Title
                if (dlim >= 0)
                {
                    Title = Title.Substring(0, dlim).TrimEnd();
                }
            }
            if (string.IsNullOrWhiteSpace(Title))
            {
                Console.WriteLine("try again!");
                Title = (HtmlDoc.DocumentNode.SelectSingleNode("//head/meta[@name='twitter:title']")?.GetAttributeValue("content", null))
                ?? HtmlDoc.DocumentNode.SelectSingleNode("//head/meta[@Property='og:title']")?.GetAttributeValue("content", null)
                //?? "UnknownTitle"
                ;
            }
        }

        void LoadDoc(string url, string path)
        {
            ReqUri = new Uri(url, UriKind.Absolute);            // must be absolute (no BaseAddress influence yet)
                                                                //  also initialises BaseAddress for any subsequent relative Urls
            HtmlDoc = new HtmlDocument
            {
                OptionEmptyCollection = true                    // SelectNodes method will return empty collection when
            };                                                  //   no node matched the XPath expression
            HtmlDoc.Load(path);

            var b = HtmlDoc.DocumentNode.SelectSingleNode("//head/base");    // there can only be ONE or none
            if (b != null)
            {
                var b2 = b.Attributes["href"];
                var tmpuri = new Uri(b2.Value, UriKind.RelativeOrAbsolute);
                if (tmpuri.IsAbsoluteUri)
                {
                    BaseAddress = tmpuri;
                }
                else
                {
                    Console.WriteLine($"base ({b2}) fail");  // 
                }
            }
        }

        void AlterLinks(string att, Dictionary<string, string> oldNewLinks)
        {
            foreach (var href in HtmlDoc.DocumentNode.SelectNodes($"//*[@{att}]"))
            {
                var url = href.Attributes[att].Value;
#if DEBUG
                if (url.StartsWith("//"))
                {
                    Console.WriteLine("observe relative scheme!");
                }
#endif
                var uri = new Uri(BaseAddress, url);                        // this handles relative scheme e.g.
                                                                            //  "//www.slideshare.net/jeremylikness/herding-cattle-with-azure-container-service-acs"
                if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)         // ignore "mailto" "javascript" etc
                    || href.Attributes["rel"]?.Value == "nofollow")
                {
                    continue;
                }
                // attempt extracting the [next] url, but ignore any exceptions
                try
                {
                    url = uri.AbsoluteUri.ToLower();                        // convert back to basic string
                    if (!oldNewLinks.TryGetValue(url, out var replurl))     // ContainsKey does not have ignoreCase but Repository.PutWebPage does
                    {
                        continue;                                           // don't change any uninteresting link (not even make relative)
                    }
                    uri = new Uri(replurl, UriKind.RelativeOrAbsolute);
                    if (uri.IsAbsoluteUri)
                    {
                        var reluri = BaseAddress.MakeRelativeUri(uri);
                        if (replurl != reluri.AbsoluteUri)
                        {
                            replurl = reluri.AbsoluteUri;
                            href.Attributes[att].Value = replurl;
                        }
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("failure extracting/updating single url, continuing anyway");
                }
            }
        }

        public void ReworkLinks(string url, string filespec, Dictionary<string, string> oldNewLinks)
        {
            LoadDoc(url, filespec);                 // load file into HtmlDoc, and set ReqUri, BaseAddress
            foreach (var att in LinkAttList)
            {
                AlterLinks(att, oldNewLinks);
            }
        }
    }
}
