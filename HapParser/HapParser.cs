﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using HtmlAgilityPack;
using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;

namespace HapLib
{
    public class HapParser : IHttpParser
    {
        static readonly char[] CRLF = { '\r', '\n' };
        const string EXTN_SEPARATOR = ".";
        static readonly string[] LinkAttList = new string[] { "cite", "data", "href", "src", "srcset" };
        const int MAX_PATH = 260;                                       // max size for device+directory+file spec but cf. WebPage.FILESIZE

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

        void FindLinks(string att, IDictionary<string, string> Links)
        {
            var ReqUriMatch = Utils.NoTrailSlash(ReqUri.GetComponents(MatchBits, UriFormat.SafeUnescaped));     // ignore Scheme|UserInfo|Fragment TODO: passes thru Scheme!
            foreach (var hnode in HtmlDoc.DocumentNode.SelectNodes($"//*[@{att}]"))
            {
                var url = hnode.Attributes[att].Value;
#if DEBUG  && BORING
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
                        || hnode.Attributes["rel"]?.Value == "nofollow")
                    {
                        continue;
                    }

                    url = Utils.NoTrailSlash(uri.GetComponents(MatchBits, UriFormat.SafeUnescaped));     // ignore Scheme|UserInfo|Fragment TODO: passes thru Scheme!
                    if (url.Length > WebPage.URLSIZE)
                    {
                        Console.WriteLine($"oversize URL:\tU={url.Length}");

                        url = SlimQP(url);
                    }
                    if (url.Equals(ReqUriMatch, StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;                                               // skip any self-refs (e.g. with fragment) or as rel=canonical
                    }
                    var protofn = FindFilespec(hnode) ?? Utils.TrimOrNull(uri.Segments[uri.Segments.Length - 1]);
                    if (string.IsNullOrEmpty(protofn))
                    {
                        foreach (var child in hnode.ChildNodes)
                        {
                            protofn = FindFilespec(child);
                            if (protofn != null)
                            {
                                break;
                            }
                        }
                    }
                    string filename = "unknown", extn = null;
                    if (string.IsNullOrEmpty(protofn))
                    {
                        Console.WriteLine($"no filename for ({hnode.OuterHtml})");
                    }
                    else
                    {
                        (filename, extn) = Utils.FileExtSplit(WebUtility.HtmlDecode(protofn));
                        (var filename2, var extn2) = Utils.FileExtSplit(WebUtility.UrlDecode(protofn));
                    }

                    if (filename != null)
                    {
                        if (string.IsNullOrWhiteSpace(extn))        // #1 if explicit extn given then we use it
                        {
                            // #2 explicit "type" ?
                            var atribType = hnode.Attributes["type"]?.Value;
                            if (atribType != null)
                            {
                                //extn = Utils.LookupExtnFromMime(atribType);
                                extn = Infrastructure.Models.MimeCollection.LookupExtnFromMime(atribType);
                            }
                            // #3 <tag att='template'> lookup ?
                            if (extn == null)
                            {
                                extn = TagAttributeLookup(hnode, hnode.Name + "-" + att);
                            }
                        }
                        if (extn == null)
                        {
                            Console.WriteLine($"no extn for ({hnode.OuterHtml}");
                        }
                        else
                        {
                            filename += EXTN_SEPARATOR + extn;
                        }
                    }
                    //filename = Utils.MakeValid(filename);       // remove any spurious chars ***now done by FileExtsplit***
                    if (filename?.Length > MAX_PATH)
                    {
                        Console.WriteLine($"oversize FILE:\tFlen={filename.Length},\tF={filename},\tU={url}");
                        filename = filename.Substring(0, MAX_PATH);
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
                catch (Exception)
                {
                    Console.WriteLine("failure extracting single url, continuing anyway");
                }
            }
        }

        private static string FindFilespec(HtmlNode hnode)
        {
            var protofn = Utils.TrimOrNull(hnode.GetAttributeValue("download", null))
                            ?? Utils.TrimOrNull(hnode.GetAttributeValue("title", null))
                            ?? FirstLine(hnode.InnerText);
            return (protofn != null) ? WebUtility.HtmlDecode(protofn) : null;
        }

        private static string FirstLine(string intext)
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

        void LoadDoc(string url, string path)
        {
            ReqUri = new Uri(url, UriKind.Absolute);            // must be absolute (no BaseAddress influence yet)
                                                                //  also initialises BaseAddress for any subsequent relative Urls
            HtmlDoc = new HtmlDocument
            {
                OptionEmptyCollection = true                    // SelectNodes method will return empty collection when
            };                                                  //   no node matched the XPath expression
            HtmlDoc.Load(path);

            var b = HtmlDoc.DocumentNode.SelectSingleNode("//head/base");    // there can only be ONE or none [href]
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
                    Console.WriteLine($"base ({b2}) fail");
                }
            }
        }

        public void LoadFromFile(string url, string path)
        {
            LoadDoc(url, path);                     // load file into HtmlDoc, and set ReqUri, BaseAddress
            // extract title if any
            Title = HtmlDoc.DocumentNode.SelectSingleNode("//head/title")?.GetAttributeValue("title", null);
            if (Title == null)
            {
                Console.WriteLine("try again!");
                Title = Utils.TrimOrNull(HtmlDoc.DocumentNode.SelectSingleNode("//head/meta[@name='twitter:title']")?.GetAttributeValue("content", null))
                    ?? Utils.TrimOrNull(HtmlDoc.DocumentNode.SelectSingleNode("//head/meta[@Property='og:title']")?.GetAttributeValue("content", null))
                //  ?? "UnknownTitle"               // don't do this (cf. Downloader.filespec4)
                ;
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

        public void SaveFile(string filespec) => HtmlDoc.Save(filespec);

        /*
        "https://c.s-microsoft.com/en-us/CMSStyles/style.csx?k=3c9ade18-bc6a-b6bd-84c3-fc69aaaa7520_899796fc-1ab6-ed87-096b-4f10b915033c_e8d8727e-02f3-1a80-54c3-f87750a8c4de_6e5b2ac7-688a-4a18-9695-a31e8139fa0f_b3dad3e4-0853-1041-fa46-2e9d6598a584_fc29d27f-7342-9cf3-c2b5-a04f30605f03_28863b11-6a1b-a28c-4aab-c36e3deb3375_907fa087-b443-3de8-613e-b445338dad1f_a66bb9d1-7095-dfc6-5a12-849441da475c_1b0ca1a3-6da9-0dbf-9932-198c9f68caeb_ef11258b-15d1-8dab-81d5-8d18bc3234bc_11339d5d-cf04-22ad-4987-06a506090313_50edf96d-7437-c38c-ad33-ebe81b170501_8031d0e3-4981-8dbc-2504-bbd5121027b7_3f0c3b77-e132-00a5-3afc-9a2f141e9eae_aebeacd9-6349-54aa-9608-cb67eadc2d17_0cdb912f-7479-061d-e4f3-bea46f10a753_343d1ae8-c6c4-87d3-af9d-4720b6ea8f34_a905814f-2c84-2cd4-839e-5634cc0cc383_190a3885-bf35-9fab-6806-86ce81df76f6_05c744db-5e3d-bcfb-75b0-441b9afb179b_8beffb66-d700-2891-2c8d-02e40c7ac557_f2be0b5b-cb09-7419-2469-40333971901d_8e7f567d-245e-5dce-919d-1da2084a1db6_04cdd06f-491b-f252-4816-e05dbe3089b4_4d591b90-4f6b-d61a-3fe3-eeabaa54e007_d2a7617d-4fec-e271-3b3c-29c71d1edda1_c54c82ad-9a34-5e14-9f7e-f76e05daa48e_7662fbc3-5b00-dd7a-8c24-6b7bb7bb4b48_2bcd3d2d-6832-7053-3643-75fe6bb53d16_90b9cae5-0156-65e5-3652-a23ad05aa89b_0eea7408-d405-33d1-b3a3-e68154c11931_ba0d0603-e291-f64d-1224-c7179a0128a3_66db1513-3061-60df-c963-21f539556ce2_0f67a2ff-4303-729b-5e92-8c9fdf41f487_edaa7a2f-8af9-ec7d-b92f-7f1d2beb1752_8458a62c-bedc-f933-0122-e66265888317_2bb2f93a-070c-24f3-a072-d242d5ed2dc6_b330fd3d-1e8a-d40d-de4a-4d1c63486b10_60605f77-9b7b-d9fe-129c-c4937ddd200a_234e2194-00bc-d945-f90c-5cb0949c5e6c_4076ed7b-5976-2d30-bc99-664dbea0b3de_54a5d793-aac7-b19e-ed26-cc0395a49b4f_2d1729a6-67a8-5390-69d1-3988a55a41c8_566cb4db-502d-4e3f-7ce4-c42a70b31054_30db642a-887e-7424-636a-671576ac660e_3684062b-2b09-fd4e-0f3e-6a149539f0c8_23c3ae93-fc96-f39a-cb7e-0a9eee5d9678_d6bdf6a8-b29b-b551-3bca-52d5615a2c54_43047ac2-d851-7cba-7f5a-f4cccf880b75_e292f94c-d076-c785-75aa-b08b99af979d_523535ac-2c9d-9841-34b2-eac6d7b47be3"
        "https://c.s-microsoft.com/en-us/CMSScripts/script.jsx?k=0502864a-b6ef-2f14-9f8e-267004d3a4e0_c5ea3348-55af-729a-2641-14f0312bacf3_742bd11f-3d7c-9955-3df5-f02b66689699_cb9d43d2-fbae-5b5c-827f-72166d6b87fc_49488e0d-6ae2-5101-c995-f4d56443b1d8_7dea7b90-4334-c043-b252-9f132d19ee19_38aa9ffb-ddb5-75be-6536-a58628f435f5_e3e65a0a-c133-43e7-571d-2293e03f85e6_4ca0e9dc-a4de-17ba-f0de-d1d346cb99e2_06310cd8-41c6-3b11-4645-b4884789ed70_5c27e8aa-9347-969e-39ac-37a4de428a8d_d6872b5a-5310-a73c-7cb3-227a3213a1c5_be92d794-4118-193f-9871-58b72092a5ac_64c742e2-b29c-b6c1-fdd9-accf33ec40bd_cf2ceca9-3467-a5b3-d095-68958eee6d4c_cec39dd8-f1d3-56f1-abfc-a7db34ff7b46_ec5fa2c9-3950-ff57-a5c3-1fa77e0db190_d19f9592-65df-bcc9-e30e-439b875c3381_76a3d06f-f11f-77ef-9bfd-6227ba750200_5e1caa45-461c-3b04-f88b-8cd50af16db5_c2dceda8-20b4-7d3f-13b6-9cac67d7df17_914fa41b-cc86-d3b0-4e15-2fdfa357bcc7_40c6c884-da6e-7c2c-081f-4a7dfe7c7245_10102c22-b3f8-db84-b802-423fccfef217_0d0bc397-9ed4-1790-c53b-19ef58e50eda_daf547ea-e7e0-5c13-2375-876773f4442e_ed1edc1e-59a4-d30a-33f1-7023ad077a46_31f7b2e8-247c-8192-8a93-02446f7ecb54_b5687080-802a-ed0f-42f6-40dddfa471e8_206c0c39-86a6-7517-32a6-297492d1134e_eb51f80f-943f-3709-b39b-d5334d3a8d75_1c034b1c-7863-2cf2-c847-70db871b2033_587d79f0-4783-6625-8f1a-7749e17b2133_cbe92ffe-1bd0-f1c6-bfb4-8d97cccdbd14_c398a8a9-5658-61a7-cff4-0c051e593636_907accee-265d-6812-c262-5ed718394b1f_7abadbf5-0ec4-418e-738e-bf850a27b554_c2652ec3-eb7e-4431-92c4-1bf6abff2a5e_89211884-15f5-1331-a947-bbd3e9418646_f12ef0bd-63fc-66af-3473-602f62d29b31_d916c9bd-addd-3124-e75c-c1bc3f494f7b_c7973a35-c684-58ce-2df4-2839f575903d"
        "https://c.s-microsoft.com/en-us/CMSStyles/style.csx?k=3c9ade18-bc6a-b6bd-84c3-fc69aaaa7520_899796fc-1ab6-ed87-096b-4f10b915033c_e8d8727e-02f3-1a80-54c3-f87750a8c4de_6e5b2ac7-688a-4a18-9695-a31e8139fa0f_b3dad3e4-0853-1041-fa46-2e9d6598a584_fc29d27f-7342-9cf3-c2b5-a04f30605f03_28863b11-6a1b-a28c-4aab-c36e3deb3375_907fa087-b443-3de8-613e-b445338dad1f_a66bb9d1-7095-dfc6-5a12-849441da475c_1b0ca1a3-6da9-0dbf-9932-198c9f68caeb_ef11258b-15d1-8dab-81d5-8d18bc3234bc_11339d5d-cf04-22ad-4987-06a506090313_50edf96d-7437-c38c-ad33-ebe81b170501_8031d0e3-4981-8dbc-2504-bbd5121027b7_3f0c3b77-e132-00a5-3afc-9a2f141e9eae_aebeacd9-6349-54aa-9608-cb67eadc2d17_0cdb912f-7479-061d-e4f3-bea46f10a753_343d1ae8-c6c4-87d3-af9d-4720b6ea8f34_a905814f-2c84-2cd4-839e-5634cc0cc383_190a3885-bf35-9fab-6806-86ce81df76f6_05c744db-5e3d-bcfb-75b0-441b9afb179b_8beffb66-d700-2891-2c8d-02e40c7ac557_f2be0b5b-cb09-7419-2469-40333971901d_8e7f567d-245e-5dce-919d-1da2084a1db6_04cdd06f-491b-f252-4816-e05dbe3089b4_4d591b90-4f6b-d61a-3fe3-eeabaa54e007_d2a7617d-4fec-e271-3b3c-29c71d1edda1_c54c82ad-9a34-5e14-9f7e-f76e05daa48e_7662fbc3-5b00-dd7a-8c24-6b7bb7bb4b48_2bcd3d2d-6832-7053-3643-75fe6bb53d16_90b9cae5-0156-65e5-3652-a23ad05aa89b_0eea7408-d405-33d1-b3a3-e68154c11931_ba0d0603-e291-f64d-1224-c7179a0128a3_66db1513-3061-60df-c963-21f539556ce2_0f67a2ff-4303-729b-5e92-8c9fdf41f487_edaa7a2f-8af9-ec7d-b92f-7f1d2beb1752_8458a62c-bedc-f933-0122-e66265888317_2bb2f93a-070c-24f3-a072-d242d5ed2dc6_b330fd3d-1e8a-d40d-de4a-4d1c63486b10_60605f77-9b7b-d9fe-129c-c4937ddd200a_234e2194-00bc-d945-f90c-5cb0949c5e6c_4076ed7b-5976-2d30-bc99-664dbea0b3de_54a5d793-aac7-b19e-ed26-cc0395a49b4f_2d1729a6-67a8-5390-69d1-3988a55a41c8_566cb4db-502d-4e3f-7ce4-c42a70b31054_30db642a-887e-7424-636a-671576ac660e_3684062b-2b09-fd4e-0f3e-6a149539f0c8_23c3ae93-fc96-f39a-cb7e-0a9eee5d9678_d6bdf6a8-b29b-b551-3bca-52d5615a2c54_43047ac2-d851-7cba-7f5a-f4cccf880b75_e292f94c-d076-c785-75aa-b08b99af979d_523535ac-2c9d-9841-34b2-eac6d7b47be3"
        "https://c.s-microsoft.com/en-us/CMSScripts/script.jsx?k=0502864a-b6ef-2f14-9f8e-267004d3a4e0_c5ea3348-55af-729a-2641-14f0312bacf3_742bd11f-3d7c-9955-3df5-f02b66689699_cb9d43d2-fbae-5b5c-827f-72166d6b87fc_49488e0d-6ae2-5101-c995-f4d56443b1d8_7dea7b90-4334-c043-b252-9f132d19ee19_38aa9ffb-ddb5-75be-6536-a58628f435f5_e3e65a0a-c133-43e7-571d-2293e03f85e6_4ca0e9dc-a4de-17ba-f0de-d1d346cb99e2_06310cd8-41c6-3b11-4645-b4884789ed70_5c27e8aa-9347-969e-39ac-37a4de428a8d_d6872b5a-5310-a73c-7cb3-227a3213a1c5_be92d794-4118-193f-9871-58b72092a5ac_64c742e2-b29c-b6c1-fdd9-accf33ec40bd_cf2ceca9-3467-a5b3-d095-68958eee6d4c_cec39dd8-f1d3-56f1-abfc-a7db34ff7b46_ec5fa2c9-3950-ff57-a5c3-1fa77e0db190_d19f9592-65df-bcc9-e30e-439b875c3381_76a3d06f-f11f-77ef-9bfd-6227ba750200_5e1caa45-461c-3b04-f88b-8cd50af16db5_c2dceda8-20b4-7d3f-13b6-9cac67d7df17_914fa41b-cc86-d3b0-4e15-2fdfa357bcc7_40c6c884-da6e-7c2c-081f-4a7dfe7c7245_10102c22-b3f8-db84-b802-423fccfef217_0d0bc397-9ed4-1790-c53b-19ef58e50eda_daf547ea-e7e0-5c13-2375-876773f4442e_ed1edc1e-59a4-d30a-33f1-7023ad077a46_31f7b2e8-247c-8192-8a93-02446f7ecb54_b5687080-802a-ed0f-42f6-40dddfa471e8_206c0c39-86a6-7517-32a6-297492d1134e_eb51f80f-943f-3709-b39b-d5334d3a8d75_1c034b1c-7863-2cf2-c847-70db871b2033_587d79f0-4783-6625-8f1a-7749e17b2133_cbe92ffe-1bd0-f1c6-bfb4-8d97cccdbd14_c398a8a9-5658-61a7-cff4-0c051e593636_907accee-265d-6812-c262-5ed718394b1f_7abadbf5-0ec4-418e-738e-bf850a27b554_c2652ec3-eb7e-4431-92c4-1bf6abff2a5e_89211884-15f5-1331-a947-bbd3e9418646_f12ef0bd-63fc-66af-3473-602f62d29b31_d916c9bd-addd-3124-e75c-c1bc3f494f7b_c7973a35-c684-58ce-2df4-2839f575903d"	string
        "https://c.s-microsoft.com/en-us/CMSStyles/style.csx?k=3c9ade18-bc6a-b6bd-84c3-fc69aaaa7520_899796fc-1ab6-ed87-096b-4f10b915033c_e8d8727e-02f3-1a80-54c3-f87750a8c4de_6e5b2ac7-688a-4a18-9695-a31e8139fa0f_b3dad3e4-0853-1041-fa46-2e9d6598a584_fc29d27f-7342-9cf3-c2b5-a04f30605f03_28863b11-6a1b-a28c-4aab-c36e3deb3375_907fa087-b443-3de8-613e-b445338dad1f_a66bb9d1-7095-dfc6-5a12-849441da475c_1b0ca1a3-6da9-0dbf-9932-198c9f68caeb_ef11258b-15d1-8dab-81d5-8d18bc3234bc_11339d5d-cf04-22ad-4987-06a506090313_50edf96d-7437-c38c-ad33-ebe81b170501_8031d0e3-4981-8dbc-2504-bbd5121027b7_3f0c3b77-e132-00a5-3afc-9a2f141e9eae_aebeacd9-6349-54aa-9608-cb67eadc2d17_0cdb912f-7479-061d-e4f3-bea46f10a753_343d1ae8-c6c4-87d3-af9d-4720b6ea8f34_a905814f-2c84-2cd4-839e-5634cc0cc383_190a3885-bf35-9fab-6806-86ce81df76f6_05c744db-5e3d-bcfb-75b0-441b9afb179b_8beffb66-d700-2891-2c8d-02e40c7ac557_f2be0b5b-cb09-7419-2469-40333971901d_8e7f567d-245e-5dce-919d-1da2084a1db6_04cdd06f-491b-f252-4816-e05dbe3089b4_4d591b90-4f6b-d61a-3fe3-eeabaa54e007_d2a7617d-4fec-e271-3b3c-29c71d1edda1_c54c82ad-9a34-5e14-9f7e-f76e05daa48e_7662fbc3-5b00-dd7a-8c24-6b7bb7bb4b48_2bcd3d2d-6832-7053-3643-75fe6bb53d16_90b9cae5-0156-65e5-3652-a23ad05aa89b_0eea7408-d405-33d1-b3a3-e68154c11931_ba0d0603-e291-f64d-1224-c7179a0128a3_66db1513-3061-60df-c963-21f539556ce2_0f67a2ff-4303-729b-5e92-8c9fdf41f487_edaa7a2f-8af9-ec7d-b92f-7f1d2beb1752_8458a62c-bedc-f933-0122-e66265888317_2bb2f93a-070c-24f3-a072-d242d5ed2dc6_b330fd3d-1e8a-d40d-de4a-4d1c63486b10_60605f77-9b7b-d9fe-129c-c4937ddd200a_234e2194-00bc-d945-f90c-5cb0949c5e6c_4076ed7b-5976-2d30-bc99-664dbea0b3de_54a5d793-aac7-b19e-ed26-cc0395a49b4f_2d1729a6-67a8-5390-69d1-3988a55a41c8_566cb4db-502d-4e3f-7ce4-c42a70b31054_30db642a-887e-7424-636a-671576ac660e_3684062b-2b09-fd4e-0f3e-6a149539f0c8_23c3ae93-fc96-f39a-cb7e-0a9eee5d9678_d6bdf6a8-b29b-b551-3bca-52d5615a2c54_43047ac2-d851-7cba-7f5a-f4cccf880b75_e292f94c-d076-c785-75aa-b08b99af979d_523535ac-2c9d-9841-34b2-eac6d7b47be3"
        "https://c.s-microsoft.com/en-us/CMSScripts/script.jsx?k=0502864a-b6ef-2f14-9f8e-267004d3a4e0_c5ea3348-55af-729a-2641-14f0312bacf3_742bd11f-3d7c-9955-3df5-f02b66689699_cb9d43d2-fbae-5b5c-827f-72166d6b87fc_49488e0d-6ae2-5101-c995-f4d56443b1d8_7dea7b90-4334-c043-b252-9f132d19ee19_38aa9ffb-ddb5-75be-6536-a58628f435f5_e3e65a0a-c133-43e7-571d-2293e03f85e6_4ca0e9dc-a4de-17ba-f0de-d1d346cb99e2_06310cd8-41c6-3b11-4645-b4884789ed70_5c27e8aa-9347-969e-39ac-37a4de428a8d_d6872b5a-5310-a73c-7cb3-227a3213a1c5_be92d794-4118-193f-9871-58b72092a5ac_64c742e2-b29c-b6c1-fdd9-accf33ec40bd_cf2ceca9-3467-a5b3-d095-68958eee6d4c_cec39dd8-f1d3-56f1-abfc-a7db34ff7b46_ec5fa2c9-3950-ff57-a5c3-1fa77e0db190_d19f9592-65df-bcc9-e30e-439b875c3381_76a3d06f-f11f-77ef-9bfd-6227ba750200_5e1caa45-461c-3b04-f88b-8cd50af16db5_c2dceda8-20b4-7d3f-13b6-9cac67d7df17_914fa41b-cc86-d3b0-4e15-2fdfa357bcc7_40c6c884-da6e-7c2c-081f-4a7dfe7c7245_10102c22-b3f8-db84-b802-423fccfef217_0d0bc397-9ed4-1790-c53b-19ef58e50eda_daf547ea-e7e0-5c13-2375-876773f4442e_ed1edc1e-59a4-d30a-33f1-7023ad077a46_31f7b2e8-247c-8192-8a93-02446f7ecb54_b5687080-802a-ed0f-42f6-40dddfa471e8_206c0c39-86a6-7517-32a6-297492d1134e_eb51f80f-943f-3709-b39b-d5334d3a8d75_1c034b1c-7863-2cf2-c847-70db871b2033_587d79f0-4783-6625-8f1a-7749e17b2133_cbe92ffe-1bd0-f1c6-bfb4-8d97cccdbd14_c398a8a9-5658-61a7-cff4-0c051e593636_907accee-265d-6812-c262-5ed718394b1f_7abadbf5-0ec4-418e-738e-bf850a27b554_c2652ec3-eb7e-4431-92c4-1bf6abff2a5e_89211884-15f5-1331-a947-bbd3e9418646_f12ef0bd-63fc-66af-3473-602f62d29b31_d916c9bd-addd-3124-e75c-c1bc3f494f7b_c7973a35-c684-58ce-2df4-2839f575903d"
        */
        static string SlimQP(string url)
        {
            const string AMP = "&amp;", QUEST = "?";
            var DELIM = new string[] { AMP };
            var qpstart = url.IndexOf(QUEST);
            if (qpstart < 0)
            {
                return url.Substring(0, WebPage.URLSIZE);           // crude truncate at max width (may not be at word-break)
            }
            var sb = new StringBuilder(url.Substring(0, qpstart));
            var qryprns = url.Substring(qpstart + 1).Split(DELIM, StringSplitOptions.RemoveEmptyEntries);
            var special = QUEST;                                    // 1st delimiter introducing QueryParams is a "?"
            for (var i = 0; i < qryprns.Length; i++)
            {
                if (!qryprns[i].EndsWith("="))
                {
                    var qpn = special + qryprns[i];
                    if (sb.Length + qpn.Length <= WebPage.URLSIZE)
                    {
                        sb.Append(qpn);                             // may not be left-significant (e.g. ignore QP[i] but copy QP[i+1])
                        special = AMP;                              // subsequent delimiter for QS params is the "&"
                    }
                }
            }
            return sb.ToString();
        }

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
    }
}