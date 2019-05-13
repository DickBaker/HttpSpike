using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using HtmlAgilityPack;
using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;

namespace HapLib
{
    public class HapParser : IHttpParser
    {
        static readonly char[] CRLF = { '\r', '\n' };
        static readonly char[] DIRSEP = { Path.DirectorySeparatorChar };

        const string EXTN_SEPARATOR = ".";
        static readonly string[] LinkAttList = { "cite", "data", "href", "src", "srcset" };
        static readonly char[] DELIMS = { '/', '?', '&', '=' };         // candidate break chars for finding last segment for DraftFilespec

        public HtmlDocument HtmlDoc { get; set; }

        const UriComponents MATCHBITS = UriComponents.Host | UriComponents.PathAndQuery;    // flags denoting significant components for equality [Host | LocalPath | Query]
                                                                                            //  but NOT Scheme (cf. GetUrlFull) | UserInfo | Fragment or Port (cf. GetUrlStandard)
        public Uri BaseAddress { get; set; }
        Uri reqUri;

        public Uri ReqUri
        {
            get => reqUri;
            set
            {
                reqUri = BaseAddress = string.IsNullOrEmpty(value.Fragment)
                   ? value
                   : new Uri(value.GetLeftPart(UriPartial.Query), UriKind.Absolute);        // scheme, authority, path, and query segments of the URI
                ReqUriMatch = GetUrlStandard(ReqUri);           // standardise on Url content for self-comparison
            }
        }

        string ReqUriMatch;

        string _title;

        /// <summary>
        ///     HTML document title or null (never blank)
        /// </summary>
        /// <remarks>
        ///     accessor will perform HtmlDecode as it should be used as basis for WebPage.DefaultFilespec
        ///     but caller's responsibility to eliminate ":", "?", "*" and other special chars
        /// </remarks>
        public string Title
        {
            get => _title;
            set
            {
                int delim;
                _title = Utils.TrimOrNull(WebUtility.HtmlDecode(value));        // remove any leading/trailing whitespace (incl CR/LF)
                if (_title != null && (delim = _title.IndexOfAny(CRLF)) > 0)    // any embedded CRLF within revised Title ?
                {
                    _title = _title.Substring(0, delim).TrimEnd();
                }
            }
        }

        int MaxLinks;                                               // max number of links (href=url etc) to extract [ignore subsequent ones]

        public HapParser(int maxlinks = 500)
        {
            MaxLinks = maxlinks < 2500 ? maxlinks : 2500;           // set arbitrary ceiling to 2500 (BULKINSERT fragile on more)
        }
        bool AlterLinks(string dirname, string att, IDictionary<string, string> oldNewLinks)    // caller should respect MaxLinks but not enforced at AlterLinks time
        {
            var rslt = false;
            foreach (var hnode in HtmlDoc.DocumentNode.SelectNodes($"//*[@{att}]"))
            {
                var url = hnode.Attributes[att].Value;
                // attempt extracting the [next] url, but ignore any exceptions
                try
                {
                    var uri = new Uri(BaseAddress, url);                    // this handles relative scheme e.g.
                                                                            //  "//www.slideshare.net/jeremylikness/herding-cattle-with-azure-container-service-acs"
                    url = GetUrlStandard(uri);                              // standardise on Url content for self-comparison
                    var schemeUrl = uri.Scheme + Uri.SchemeDelimiter + url;
                    if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)         // ignore "mailto" "javascript" etc
                        || hnode.Attributes["rel"]?.Value == "nofollow"
                        || url.Length > WebPage.URLSIZE                        // oversize URL ?
                        || url.Equals(ReqUriMatch, StringComparison.InvariantCultureIgnoreCase) // skip any self-refs (e.g. with fragment) or as rel=canonical
                        || !oldNewLinks.TryGetValue(schemeUrl, out var fsabs))    // ContainsKey does not have ignoreCase but Repository.PutWebPage does
                    {
                        continue;                                           // don't change any uninteresting link (not even make relative)
                    }
                    var fsrel = Utils.GetRelativePath(dirname, fsabs);
                    if (uri.Fragment.CompareTo("#") > 0)                    // ignore null, space, "#" but catch "#frag"
                    {
                        Console.WriteLine("postfix fragment");
                        fsrel += uri.Fragment;              // TODO: does this need "#" ??
                    }
                    hnode.Attributes[att].Value = fsrel;
                    rslt = true;
                }
                catch (Exception)
                {
                    Console.WriteLine("failure extracting/updating single url, continuing anyway");
                }
            }
            return rslt;
        }

        void FindLinks(string att, IDictionary<string, string> Links)   // this code enforces MaxLinks but not enforced at AlterLinks time
        {
            char[] setsplit = { ',' };
            foreach (var hnode in HtmlDoc.DocumentNode.SelectNodes($"//*[@{att}]"))
            {
                var url = hnode.Attributes[att].Value.Trim();           // deal with "//\r\ne.issuu.com/embed.js" oddballs
                var odduri = new Uri(BaseAddress, url);
                // special case for srcset that can contain multiple URLs
                if (att == "srcset")
                {
                    var sets = url.Split(setsplit, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var set in sets)
                    {
                        url = set.Trim();
                        var setscope = string.Empty;
                        var dlim = url.IndexOf(' ');
                        if (dlim > 0)
                        {
                            setscope = url.Substring(dlim + 1).TrimStart();     // extract media condition first
                            url = url.Substring(0, dlim).TrimEnd();             //  then subset the actual URL
                        }
                        AppendLink(att, hnode, url, Links);
                    }
                }
                else
                {
                    AppendLink(att, hnode, url, Links);
                }
                if (Links.Count > MaxLinks)
                {
                    break;                                                      // avoid fragile BULKINSERT
                }
            }
        }

        void AppendLink(string att, HtmlNode hnode, string url, IDictionary<string, string> Links)
        {
            // attempt extracting the [next] url, but ignore any exceptions
            try
            {
                var uri = new Uri(BaseAddress, url);                        // this handles relative scheme e.g.
                                                                            //  "//www.slideshare.net/jeremylikness/herding-cattle-with-azure-container-service-acs"
                url = GetUrlStandard(uri);                                  // standardise on Url content for self-comparison
                if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)   // ignore "mailto" "javascript" etc "mailto:jestedfa%40microsoft.com?Subject=MailKit Documentation"
                    || hnode.Attributes["rel"]?.Value == "nofollow"
                    || url.Equals(ReqUriMatch, StringComparison.InvariantCultureIgnoreCase))
                {
                    return;                                               // skip any self-refs (e.g. with fragment) or as rel=canonical
                }

                // assign DraftFilespec via choice skip-chain [N.B. Downloader can set Filespec after download
                //  if rsp.Content.Headers.ContentDisposition.FileName or Title (from //head/title[title]) looks more suitable]
                var numsegs = uri.Segments.Length;
                var protofn = numsegs > 0
                    ? Utils.TrimOrNull(uri.Segments[numsegs - 1])           // #1 prefer last segment
                    : null;
                if (protofn == "/")
                {
                    protofn = numsegs > 1
                        ? Utils.TrimOrNull(uri.Segments[numsegs - 2])       // #2 or penultimate segment if last is "/"
                        : null;
                }
                if (string.IsNullOrEmpty(protofn))
                {
                    var idx = url.LastIndexOfAny(DELIMS);
                    if (idx >= 0)
                    {
                        protofn = Utils.TrimOrNull(url.Substring(idx + 1)); // #3 or any parameter
                    }
                    if (protofn == null || protofn.Length > 80)             // unless too loooong
                    {
                        var protofn2 = FindFilespec(hnode);                 // #4 .InnerText on current HNode
                        if (string.IsNullOrEmpty(protofn2))
                        {
                            foreach (var child in hnode.ChildNodes)
                            {
                                protofn2 = FindFilespec(child);             // #5 .InnerText on child HNode (depth=1 only)
                                if (protofn2 != null)
                                {
                                    break;
                                }
                            }
                        }
                        if (protofn2 != null && (protofn == null || (protofn.Length > protofn2.Length && protofn2.Length >= 30)))
                        {
                            protofn = protofn2;                                 // if #1-3 choice too long take #4-5 choice if shorter
                        }
                    }

                }
                string filename = null, extn = null;
                if (string.IsNullOrEmpty(protofn))
                {
                    Console.WriteLine($"no filename for ({hnode.OuterHtml})");
                }
                else
                {
                    (filename, extn) = Utils.FileExtSplit(WebUtility.HtmlDecode(protofn));
                    //(var filename2, var extn2) = Utils.FileExtSplit(WebUtility.UrlDecode(protofn));     // TODO: which is better ?
                }

                if (filename != null)
                {
                    if (string.IsNullOrWhiteSpace(extn))                        // #1 if explicit extn given then we use it
                    {
                        var atribType = hnode.Attributes["type"]?.Value;        // #2 explicit "type" ?
                        if (atribType != null)
                        {
                            //extn = Utils.LookupExtnFromMime(atribType);
                            extn = Infrastructure.Models.MimeCollection.LookupExtnFromMime(atribType);
                        }
                        if (extn == null)                                       // #3 <tag att='template'> lookup ?
                        {
                            extn = TagAttributeLookup(hnode, hnode.Name + "-" + att);
                        }
                    }
                    if (extn == null)
                    {
                        Console.WriteLine($"no extn for ({hnode.OuterHtml}");
                        if (filename?.Length > WebPage.DRAFTSIZE)
                        {
                            Console.WriteLine($"oversize FILE:\tFlen={filename.Length},\tF={filename},\tU={url}");
                            filename =          // filename.Substring(0, WebPage.DRAFTSIZE);
                                                // Utils.RandomFilenameOnly();  // NB this would produce a file5678 format
                                       null;    // big filename but no extn, so let Downloader invent name.extn (based on Title and Headers.ContentType)
                        }
                    }
                    else
                    {
                        if (extn.Length > ContentTypeToExtn.EXTNSIZE)
                        {
                            extn = extn.Substring(0, ContentTypeToExtn.EXTNSIZE);       // TODO: or null ??
                        }
                        if (filename.Length + EXTN_SEPARATOR.Length + extn.Length > WebPage.DRAFTSIZE)
                        {
                            Console.WriteLine($"oversize FILE:\tFlen={filename.Length},\tF={filename},\tU={url}");
                            filename =      //filename.Substring(0, WebPage.DRAFTSIZE - EXTN_SEPARATOR.Length - extn.Length)
                                Utils.RandomFilenameOnly()                  // too long means we prefer short file5678 format
                                + EXTN_SEPARATOR + extn;                    //  but keep the supposed extn for now
                        }
                        else
                        {
                            filename += EXTN_SEPARATOR + extn;
                        }
                    }
                }

                url = GetUriFull(uri);                      // recover the full Uri for persisting to db
                if (url.Length > WebPage.URLSIZE)
                {
                    Console.WriteLine($"oversize URL:\tU={url.Length}");

                    url = WebPage.SlimQP(url);              // trim [parts of] queryparams, or throw exception if still too big
                }
                if (!Links.ContainsKey(url))                // IDictionary.ContainsKey does not have ignoreCase overload, but concrete derived
                {                                           //   SortedDictionary | SortedList embeds StringComparer.InvariantCultureIgnoreCase in ctor
                    Links[url] = filename;                  // add new url plus probable extn
                }
                else
                {
                    if (Links[url] == null || (filename != null && !Links[url].Equals(filename, StringComparison.InvariantCultureIgnoreCase)))  // existing url: check matches same extn
                    {
                        if (!string.IsNullOrWhiteSpace(Links[url]))
                        {
                            Console.WriteLine($"variant file.ext for {url} : {Links[url]} {filename}");
                        }
                        Links[url] = filename;
                    }
                }
            }
            catch (Exception e)         // mailto: will probably bomb here as BaseAddress munge above will barf
            {
                Console.WriteLine($"failure extracting single url for {att} on {url}, continuing anyway\n{e.Message}");
            }
        }

        static string FindFilespec(HtmlNode hnode)
        {
            var protofn = Utils.TrimOrNull(hnode.GetAttributeValue("download", null))
                            ?? Utils.TrimOrNull(hnode.GetAttributeValue("title", null))
                            ?? FirstLine(hnode.InnerText)
                            ?? Utils.TrimOrNull(hnode.GetAttributeValue("alt", null));
            return (protofn != null) ? WebUtility.HtmlDecode(protofn) : null;
        }

        static string FirstLine(string intext)
        {
            if (string.IsNullOrWhiteSpace(intext))
            {
                return null;
            }
            intext = intext.Trim();                                 // remove leading & trailing blanks (incl CRLF)
            var delim = intext.IndexOfAny(CRLF);
            return (delim > 0)
                ? intext.Substring(0, delim).TrimEnd()              // first line only
                : intext;
        }

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

        /// <summary>
        ///     get text version of the specified Uri, omitting explicit port if standard (80/443)
        /// </summary>
        /// <param name="uri">
        ///     Uri of the current WebPage (for ReqUri) or extracted link
        /// </param>
        /// <returns>
        ///     Url for temporary self-comparison purpose (not persisted)
        /// </returns>
        /// <remarks>
        /// 1.  do not use this standardisation for self-comparison. use GetUrlStandard instead
        /// 2.  Scheme is included because we want EF to construct UriKind.Absolute in future (when reading back from db)
        /// 3.  the Fragmentfield is always omitted
        /// </remarks>
        static string GetUriFull(Uri uri) => uri.GetComponents(                 // regain the full (-ish!) AbsoluteUri address (above eliminated scheme for self-comparison)
                (uri.Scheme == Uri.UriSchemeHttp && uri.Port == 80) || (uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443)
                    ? UriComponents.Scheme | MATCHBITS                          // omit explicit port:# if standard
                    : UriComponents.Scheme | MATCHBITS | UriComponents.Port     // include explicit port:# if non-standard
               , UriFormat.SafeUnescaped);

        /// <summary>
        ///     get text version of the specified Uri, omitting Scheme and explicit port if standard (80/443)
        /// </summary>
        /// <param name="uri">
        ///     Uri of the current WebPage (for ReqUri) or extracted link
        /// </param>
        /// <returns>
        ///     Url for temporary self-comparison purpose (not persisted)
        /// </returns>
        /// <remarks>
        /// 1.  do not use this standardisation for persisting Url to db. use GetUriFull instead
        /// 2.  Scheme is omitted because FindLinks will only accept HTTP/HTTPS schemes anyway
        /// 3.  the Fragment field is always omitted
        /// 4.  TODO: ignore any trailing slash in Path ?
        /// </remarks>
        string GetUrlStandard(Uri uri)
        {
            // flags denoting significant components for equality [Host | LocalPath | Query], but NOT Scheme (cf. GetUrlFull) | UserInfo | Fragment or Port
            var uriflags = string.IsNullOrWhiteSpace(uri.Query) || uri.Query == "?"
                ? UriComponents.Host | UriComponents.Path               // drop QS
                : UriComponents.Host | UriComponents.PathAndQuery;      // pass QS

            return uri.GetComponents(
                (uri.Scheme == Uri.UriSchemeHttp && uri.Port == 80) || (uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443)
                ? uriflags                                              // drop explicit port:# if standard
                : uriflags | UriComponents.Port                         // pass explicit port:# if non-standard
                , UriFormat.SafeUnescaped);
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

            var basenode = HtmlDoc.DocumentNode.SelectSingleNode("//head/base[href]");    // there can only be ONE or none [href]
            if (basenode != null)
            {
                var baseatt = basenode.Attributes["href"];
                var baseuri = new Uri(baseatt.Value, UriKind.RelativeOrAbsolute);
                if (baseuri.IsAbsoluteUri)
                {
                    BaseAddress = baseuri;
                }
                else
                {
                    Console.WriteLine($"base ({baseatt}) fail");
                }
            }
        }

        public void LoadFromFile(string url, string path)
        {
            LoadDoc(url, path);                     // load file into HtmlDoc, and set ReqUri, BaseAddress
            // extract title if any
            var tit = HtmlDoc.DocumentNode.SelectSingleNode("//head/title");
            Title = Utils.TrimOrNull(tit?.GetAttributeValue("title", tit.InnerText))
                ?? Utils.TrimOrNull(HtmlDoc.DocumentNode.SelectSingleNode("//head/meta[@name='twitter:title']")?.GetAttributeValue("content", null))
                ?? Utils.TrimOrNull(HtmlDoc.DocumentNode.SelectSingleNode("//head/meta[@Property='og:title']")?.GetAttributeValue("content", null))
            //  ?? "UnknownTitle"               // don't do this (cf. Downloader.filespec4)
            ;
        }

        public bool ReworkLinks(string filespec, IDictionary<string, string> oldNewLinks)
        {
            var dirname = Path.GetDirectoryName(filespec) + DIRSEP;          // must have trailing slash for recognition as folder
            var changedLinks = false;
            foreach (var att in LinkAttList)
            {
                changedLinks |= AlterLinks(dirname, att, oldNewLinks);
            }
            return changedLinks;
        }

        public void SaveFile(string filespec) => HtmlDoc.Save(filespec);

        /*
        "https://c.s-microsoft.com/en-us/CMSStyles/style.csx?k=3c9ade18-bc6a-b6bd-84c3-fc69aaaa7520_899796fc-1ab6-ed87-096b-4f10b915033c_e8d8727e-02f3-1a80-54c3-f87750a8c4de_6e5b2ac7-688a-4a18-9695-a31e8139fa0f_b3dad3e4-0853-1041-fa46-2e9d6598a584_fc29d27f-7342-9cf3-c2b5-a04f30605f03_28863b11-6a1b-a28c-4aab-c36e3deb3375_907fa087-b443-3de8-613e-b445338dad1f_a66bb9d1-7095-dfc6-5a12-849441da475c_1b0ca1a3-6da9-0dbf-9932-198c9f68caeb_ef11258b-15d1-8dab-81d5-8d18bc3234bc_11339d5d-cf04-22ad-4987-06a506090313_50edf96d-7437-c38c-ad33-ebe81b170501_8031d0e3-4981-8dbc-2504-bbd5121027b7_3f0c3b77-e132-00a5-3afc-9a2f141e9eae_aebeacd9-6349-54aa-9608-cb67eadc2d17_0cdb912f-7479-061d-e4f3-bea46f10a753_343d1ae8-c6c4-87d3-af9d-4720b6ea8f34_a905814f-2c84-2cd4-839e-5634cc0cc383_190a3885-bf35-9fab-6806-86ce81df76f6_05c744db-5e3d-bcfb-75b0-441b9afb179b_8beffb66-d700-2891-2c8d-02e40c7ac557_f2be0b5b-cb09-7419-2469-40333971901d_8e7f567d-245e-5dce-919d-1da2084a1db6_04cdd06f-491b-f252-4816-e05dbe3089b4_4d591b90-4f6b-d61a-3fe3-eeabaa54e007_d2a7617d-4fec-e271-3b3c-29c71d1edda1_c54c82ad-9a34-5e14-9f7e-f76e05daa48e_7662fbc3-5b00-dd7a-8c24-6b7bb7bb4b48_2bcd3d2d-6832-7053-3643-75fe6bb53d16_90b9cae5-0156-65e5-3652-a23ad05aa89b_0eea7408-d405-33d1-b3a3-e68154c11931_ba0d0603-e291-f64d-1224-c7179a0128a3_66db1513-3061-60df-c963-21f539556ce2_0f67a2ff-4303-729b-5e92-8c9fdf41f487_edaa7a2f-8af9-ec7d-b92f-7f1d2beb1752_8458a62c-bedc-f933-0122-e66265888317_2bb2f93a-070c-24f3-a072-d242d5ed2dc6_b330fd3d-1e8a-d40d-de4a-4d1c63486b10_60605f77-9b7b-d9fe-129c-c4937ddd200a_234e2194-00bc-d945-f90c-5cb0949c5e6c_4076ed7b-5976-2d30-bc99-664dbea0b3de_54a5d793-aac7-b19e-ed26-cc0395a49b4f_2d1729a6-67a8-5390-69d1-3988a55a41c8_566cb4db-502d-4e3f-7ce4-c42a70b31054_30db642a-887e-7424-636a-671576ac660e_3684062b-2b09-fd4e-0f3e-6a149539f0c8_23c3ae93-fc96-f39a-cb7e-0a9eee5d9678_d6bdf6a8-b29b-b551-3bca-52d5615a2c54_43047ac2-d851-7cba-7f5a-f4cccf880b75_e292f94c-d076-c785-75aa-b08b99af979d_523535ac-2c9d-9841-34b2-eac6d7b47be3"
        "https://c.s-microsoft.com/en-us/CMSScripts/script.jsx?k=0502864a-b6ef-2f14-9f8e-267004d3a4e0_c5ea3348-55af-729a-2641-14f0312bacf3_742bd11f-3d7c-9955-3df5-f02b66689699_cb9d43d2-fbae-5b5c-827f-72166d6b87fc_49488e0d-6ae2-5101-c995-f4d56443b1d8_7dea7b90-4334-c043-b252-9f132d19ee19_38aa9ffb-ddb5-75be-6536-a58628f435f5_e3e65a0a-c133-43e7-571d-2293e03f85e6_4ca0e9dc-a4de-17ba-f0de-d1d346cb99e2_06310cd8-41c6-3b11-4645-b4884789ed70_5c27e8aa-9347-969e-39ac-37a4de428a8d_d6872b5a-5310-a73c-7cb3-227a3213a1c5_be92d794-4118-193f-9871-58b72092a5ac_64c742e2-b29c-b6c1-fdd9-accf33ec40bd_cf2ceca9-3467-a5b3-d095-68958eee6d4c_cec39dd8-f1d3-56f1-abfc-a7db34ff7b46_ec5fa2c9-3950-ff57-a5c3-1fa77e0db190_d19f9592-65df-bcc9-e30e-439b875c3381_76a3d06f-f11f-77ef-9bfd-6227ba750200_5e1caa45-461c-3b04-f88b-8cd50af16db5_c2dceda8-20b4-7d3f-13b6-9cac67d7df17_914fa41b-cc86-d3b0-4e15-2fdfa357bcc7_40c6c884-da6e-7c2c-081f-4a7dfe7c7245_10102c22-b3f8-db84-b802-423fccfef217_0d0bc397-9ed4-1790-c53b-19ef58e50eda_daf547ea-e7e0-5c13-2375-876773f4442e_ed1edc1e-59a4-d30a-33f1-7023ad077a46_31f7b2e8-247c-8192-8a93-02446f7ecb54_b5687080-802a-ed0f-42f6-40dddfa471e8_206c0c39-86a6-7517-32a6-297492d1134e_eb51f80f-943f-3709-b39b-d5334d3a8d75_1c034b1c-7863-2cf2-c847-70db871b2033_587d79f0-4783-6625-8f1a-7749e17b2133_cbe92ffe-1bd0-f1c6-bfb4-8d97cccdbd14_c398a8a9-5658-61a7-cff4-0c051e593636_907accee-265d-6812-c262-5ed718394b1f_7abadbf5-0ec4-418e-738e-bf850a27b554_c2652ec3-eb7e-4431-92c4-1bf6abff2a5e_89211884-15f5-1331-a947-bbd3e9418646_f12ef0bd-63fc-66af-3473-602f62d29b31_d916c9bd-addd-3124-e75c-c1bc3f494f7b_c7973a35-c684-58ce-2df4-2839f575903d"
        "https://c.s-microsoft.com/en-us/CMSStyles/style.csx?k=3c9ade18-bc6a-b6bd-84c3-fc69aaaa7520_899796fc-1ab6-ed87-096b-4f10b915033c_e8d8727e-02f3-1a80-54c3-f87750a8c4de_6e5b2ac7-688a-4a18-9695-a31e8139fa0f_b3dad3e4-0853-1041-fa46-2e9d6598a584_fc29d27f-7342-9cf3-c2b5-a04f30605f03_28863b11-6a1b-a28c-4aab-c36e3deb3375_907fa087-b443-3de8-613e-b445338dad1f_a66bb9d1-7095-dfc6-5a12-849441da475c_1b0ca1a3-6da9-0dbf-9932-198c9f68caeb_ef11258b-15d1-8dab-81d5-8d18bc3234bc_11339d5d-cf04-22ad-4987-06a506090313_50edf96d-7437-c38c-ad33-ebe81b170501_8031d0e3-4981-8dbc-2504-bbd5121027b7_3f0c3b77-e132-00a5-3afc-9a2f141e9eae_aebeacd9-6349-54aa-9608-cb67eadc2d17_0cdb912f-7479-061d-e4f3-bea46f10a753_343d1ae8-c6c4-87d3-af9d-4720b6ea8f34_a905814f-2c84-2cd4-839e-5634cc0cc383_190a3885-bf35-9fab-6806-86ce81df76f6_05c744db-5e3d-bcfb-75b0-441b9afb179b_8beffb66-d700-2891-2c8d-02e40c7ac557_f2be0b5b-cb09-7419-2469-40333971901d_8e7f567d-245e-5dce-919d-1da2084a1db6_04cdd06f-491b-f252-4816-e05dbe3089b4_4d591b90-4f6b-d61a-3fe3-eeabaa54e007_d2a7617d-4fec-e271-3b3c-29c71d1edda1_c54c82ad-9a34-5e14-9f7e-f76e05daa48e_7662fbc3-5b00-dd7a-8c24-6b7bb7bb4b48_2bcd3d2d-6832-7053-3643-75fe6bb53d16_90b9cae5-0156-65e5-3652-a23ad05aa89b_0eea7408-d405-33d1-b3a3-e68154c11931_ba0d0603-e291-f64d-1224-c7179a0128a3_66db1513-3061-60df-c963-21f539556ce2_0f67a2ff-4303-729b-5e92-8c9fdf41f487_edaa7a2f-8af9-ec7d-b92f-7f1d2beb1752_8458a62c-bedc-f933-0122-e66265888317_2bb2f93a-070c-24f3-a072-d242d5ed2dc6_b330fd3d-1e8a-d40d-de4a-4d1c63486b10_60605f77-9b7b-d9fe-129c-c4937ddd200a_234e2194-00bc-d945-f90c-5cb0949c5e6c_4076ed7b-5976-2d30-bc99-664dbea0b3de_54a5d793-aac7-b19e-ed26-cc0395a49b4f_2d1729a6-67a8-5390-69d1-3988a55a41c8_566cb4db-502d-4e3f-7ce4-c42a70b31054_30db642a-887e-7424-636a-671576ac660e_3684062b-2b09-fd4e-0f3e-6a149539f0c8_23c3ae93-fc96-f39a-cb7e-0a9eee5d9678_d6bdf6a8-b29b-b551-3bca-52d5615a2c54_43047ac2-d851-7cba-7f5a-f4cccf880b75_e292f94c-d076-c785-75aa-b08b99af979d_523535ac-2c9d-9841-34b2-eac6d7b47be3"
        "https://c.s-microsoft.com/en-us/CMSScripts/script.jsx?k=0502864a-b6ef-2f14-9f8e-267004d3a4e0_c5ea3348-55af-729a-2641-14f0312bacf3_742bd11f-3d7c-9955-3df5-f02b66689699_cb9d43d2-fbae-5b5c-827f-72166d6b87fc_49488e0d-6ae2-5101-c995-f4d56443b1d8_7dea7b90-4334-c043-b252-9f132d19ee19_38aa9ffb-ddb5-75be-6536-a58628f435f5_e3e65a0a-c133-43e7-571d-2293e03f85e6_4ca0e9dc-a4de-17ba-f0de-d1d346cb99e2_06310cd8-41c6-3b11-4645-b4884789ed70_5c27e8aa-9347-969e-39ac-37a4de428a8d_d6872b5a-5310-a73c-7cb3-227a3213a1c5_be92d794-4118-193f-9871-58b72092a5ac_64c742e2-b29c-b6c1-fdd9-accf33ec40bd_cf2ceca9-3467-a5b3-d095-68958eee6d4c_cec39dd8-f1d3-56f1-abfc-a7db34ff7b46_ec5fa2c9-3950-ff57-a5c3-1fa77e0db190_d19f9592-65df-bcc9-e30e-439b875c3381_76a3d06f-f11f-77ef-9bfd-6227ba750200_5e1caa45-461c-3b04-f88b-8cd50af16db5_c2dceda8-20b4-7d3f-13b6-9cac67d7df17_914fa41b-cc86-d3b0-4e15-2fdfa357bcc7_40c6c884-da6e-7c2c-081f-4a7dfe7c7245_10102c22-b3f8-db84-b802-423fccfef217_0d0bc397-9ed4-1790-c53b-19ef58e50eda_daf547ea-e7e0-5c13-2375-876773f4442e_ed1edc1e-59a4-d30a-33f1-7023ad077a46_31f7b2e8-247c-8192-8a93-02446f7ecb54_b5687080-802a-ed0f-42f6-40dddfa471e8_206c0c39-86a6-7517-32a6-297492d1134e_eb51f80f-943f-3709-b39b-d5334d3a8d75_1c034b1c-7863-2cf2-c847-70db871b2033_587d79f0-4783-6625-8f1a-7749e17b2133_cbe92ffe-1bd0-f1c6-bfb4-8d97cccdbd14_c398a8a9-5658-61a7-cff4-0c051e593636_907accee-265d-6812-c262-5ed718394b1f_7abadbf5-0ec4-418e-738e-bf850a27b554_c2652ec3-eb7e-4431-92c4-1bf6abff2a5e_89211884-15f5-1331-a947-bbd3e9418646_f12ef0bd-63fc-66af-3473-602f62d29b31_d916c9bd-addd-3124-e75c-c1bc3f494f7b_c7973a35-c684-58ce-2df4-2839f575903d"	string
        "https://c.s-microsoft.com/en-us/CMSStyles/style.csx?k=3c9ade18-bc6a-b6bd-84c3-fc69aaaa7520_899796fc-1ab6-ed87-096b-4f10b915033c_e8d8727e-02f3-1a80-54c3-f87750a8c4de_6e5b2ac7-688a-4a18-9695-a31e8139fa0f_b3dad3e4-0853-1041-fa46-2e9d6598a584_fc29d27f-7342-9cf3-c2b5-a04f30605f03_28863b11-6a1b-a28c-4aab-c36e3deb3375_907fa087-b443-3de8-613e-b445338dad1f_a66bb9d1-7095-dfc6-5a12-849441da475c_1b0ca1a3-6da9-0dbf-9932-198c9f68caeb_ef11258b-15d1-8dab-81d5-8d18bc3234bc_11339d5d-cf04-22ad-4987-06a506090313_50edf96d-7437-c38c-ad33-ebe81b170501_8031d0e3-4981-8dbc-2504-bbd5121027b7_3f0c3b77-e132-00a5-3afc-9a2f141e9eae_aebeacd9-6349-54aa-9608-cb67eadc2d17_0cdb912f-7479-061d-e4f3-bea46f10a753_343d1ae8-c6c4-87d3-af9d-4720b6ea8f34_a905814f-2c84-2cd4-839e-5634cc0cc383_190a3885-bf35-9fab-6806-86ce81df76f6_05c744db-5e3d-bcfb-75b0-441b9afb179b_8beffb66-d700-2891-2c8d-02e40c7ac557_f2be0b5b-cb09-7419-2469-40333971901d_8e7f567d-245e-5dce-919d-1da2084a1db6_04cdd06f-491b-f252-4816-e05dbe3089b4_4d591b90-4f6b-d61a-3fe3-eeabaa54e007_d2a7617d-4fec-e271-3b3c-29c71d1edda1_c54c82ad-9a34-5e14-9f7e-f76e05daa48e_7662fbc3-5b00-dd7a-8c24-6b7bb7bb4b48_2bcd3d2d-6832-7053-3643-75fe6bb53d16_90b9cae5-0156-65e5-3652-a23ad05aa89b_0eea7408-d405-33d1-b3a3-e68154c11931_ba0d0603-e291-f64d-1224-c7179a0128a3_66db1513-3061-60df-c963-21f539556ce2_0f67a2ff-4303-729b-5e92-8c9fdf41f487_edaa7a2f-8af9-ec7d-b92f-7f1d2beb1752_8458a62c-bedc-f933-0122-e66265888317_2bb2f93a-070c-24f3-a072-d242d5ed2dc6_b330fd3d-1e8a-d40d-de4a-4d1c63486b10_60605f77-9b7b-d9fe-129c-c4937ddd200a_234e2194-00bc-d945-f90c-5cb0949c5e6c_4076ed7b-5976-2d30-bc99-664dbea0b3de_54a5d793-aac7-b19e-ed26-cc0395a49b4f_2d1729a6-67a8-5390-69d1-3988a55a41c8_566cb4db-502d-4e3f-7ce4-c42a70b31054_30db642a-887e-7424-636a-671576ac660e_3684062b-2b09-fd4e-0f3e-6a149539f0c8_23c3ae93-fc96-f39a-cb7e-0a9eee5d9678_d6bdf6a8-b29b-b551-3bca-52d5615a2c54_43047ac2-d851-7cba-7f5a-f4cccf880b75_e292f94c-d076-c785-75aa-b08b99af979d_523535ac-2c9d-9841-34b2-eac6d7b47be3"
        "https://c.s-microsoft.com/en-us/CMSScripts/script.jsx?k=0502864a-b6ef-2f14-9f8e-267004d3a4e0_c5ea3348-55af-729a-2641-14f0312bacf3_742bd11f-3d7c-9955-3df5-f02b66689699_cb9d43d2-fbae-5b5c-827f-72166d6b87fc_49488e0d-6ae2-5101-c995-f4d56443b1d8_7dea7b90-4334-c043-b252-9f132d19ee19_38aa9ffb-ddb5-75be-6536-a58628f435f5_e3e65a0a-c133-43e7-571d-2293e03f85e6_4ca0e9dc-a4de-17ba-f0de-d1d346cb99e2_06310cd8-41c6-3b11-4645-b4884789ed70_5c27e8aa-9347-969e-39ac-37a4de428a8d_d6872b5a-5310-a73c-7cb3-227a3213a1c5_be92d794-4118-193f-9871-58b72092a5ac_64c742e2-b29c-b6c1-fdd9-accf33ec40bd_cf2ceca9-3467-a5b3-d095-68958eee6d4c_cec39dd8-f1d3-56f1-abfc-a7db34ff7b46_ec5fa2c9-3950-ff57-a5c3-1fa77e0db190_d19f9592-65df-bcc9-e30e-439b875c3381_76a3d06f-f11f-77ef-9bfd-6227ba750200_5e1caa45-461c-3b04-f88b-8cd50af16db5_c2dceda8-20b4-7d3f-13b6-9cac67d7df17_914fa41b-cc86-d3b0-4e15-2fdfa357bcc7_40c6c884-da6e-7c2c-081f-4a7dfe7c7245_10102c22-b3f8-db84-b802-423fccfef217_0d0bc397-9ed4-1790-c53b-19ef58e50eda_daf547ea-e7e0-5c13-2375-876773f4442e_ed1edc1e-59a4-d30a-33f1-7023ad077a46_31f7b2e8-247c-8192-8a93-02446f7ecb54_b5687080-802a-ed0f-42f6-40dddfa471e8_206c0c39-86a6-7517-32a6-297492d1134e_eb51f80f-943f-3709-b39b-d5334d3a8d75_1c034b1c-7863-2cf2-c847-70db871b2033_587d79f0-4783-6625-8f1a-7749e17b2133_cbe92ffe-1bd0-f1c6-bfb4-8d97cccdbd14_c398a8a9-5658-61a7-cff4-0c051e593636_907accee-265d-6812-c262-5ed718394b1f_7abadbf5-0ec4-418e-738e-bf850a27b554_c2652ec3-eb7e-4431-92c4-1bf6abff2a5e_89211884-15f5-1331-a947-bbd3e9418646_f12ef0bd-63fc-66af-3473-602f62d29b31_d916c9bd-addd-3124-e75c-c1bc3f494f7b_c7973a35-c684-58ce-2df4-2839f575903d"
        */
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
                case "button-href":
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
                        //    return "png";         // check type='image/png'
                        case "icon":
                        case "shortcut icon":
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
    }
}
