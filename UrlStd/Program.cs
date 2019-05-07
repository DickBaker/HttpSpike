using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HapLib;
using Infrastructure.Interfaces;
using Infrastructure.Models;
using Webstore;
using WebStore;

namespace UrlStd
{
    class Program
    {
        static WebModel ctx;
        static IRepository repo;
        static List<WebPage> allpages;
        static async Task Main(string[] args)
        {
            ctx = new WebModel();
            repo = new Repository(ctx);
            MimeCollection.Load(await repo.GetContentTypeToExtnsAsync());
            allpages = ctx.WebPages.ToList();

            var p = new Program();
            #region unused
            /*
    var urls = new string[] {
        "http://www.ligonier.org",                        //
        "http://www.ligonier.org/blog",
        "http://www.ligonier.org/blog/category/ministry-news",
        "http://www.ligonier.org?",                        //
        "http://www.ligonier.org/blog?",
        "http://www.ligonier.org/blog/category/ministry-news?",
        "https://www.ligonier.org",                        //
        "https://www.ligonier.org/blog",
        "https://www.ligonier.org/blog/category/ministry-news",
        "https://www.ligonier.org?",                        //
        "https://www.ligonier.org/blog?",
        "https://www.ligonier.org/blog/category/ministry-news?",
        "https://www.ligonier.org/",                        //
        "https://www.ligonier.org/blog/",                        //
        "https://www.ligonier.org/blog/category/ministry-news/",
        "https://www.ligonier.org/?",                        //
        "https://www.ligonier.org/blog/?",                        //
        "https://www.ligonier.org/blog/category/ministry-news/?",
        "https://www.ligonier.org?abc=123",                        //
        "https://www.ligonier.org/blog?abc=123",                        //
        "https://www.ligonier.org/blog/category/ministry-news?abc=123",
        "https://www.ligonier.org/?abc=123",                        //
        "https://www.ligonier.org/blog/?abc=123",                        //
        "https://www.ligonier.org/blog/category/ministry-news/?abc=123",
        "https://www.ligonier.org?abc=123",                        //
        "https://www.ligonier.org/blog?abc=123",                        //
        "https://www.ligonier.org/blog/category/ministry-news?abc=123"
    };
    foreach (var url in urls)
    {
        var u2 = StdUrl(url);
    }
    */
            #endregion

            var u = "http://www.ligonier.org/store/keyword/apologetics";
            var fs = @"C:\Ligonier\webcache\41m4uuk2.html";
            var HParser = new HapParser();
            HParser.LoadFromFile(u, fs);
            var lnks = HParser.GetLinks();

            var url0 = "http://www.ligonier.org/store/keyword/apologetics";
            var bld = new UriBuilder(url0);
            var url1 = bld.Uri.AbsoluteUri;
            if (url0 != url1)
            {
                Console.WriteLine($"{url0}\t->\t{url1}");
            }

            foreach (var webpage in allpages)
            {
                var url = webpage.Url;
                var url2 = StdUrl(url);
                LookupPage2(webpage, url, url2, changeUrl: true);

                url2 = (url2.StartsWith(Uri.UriSchemeHttp))
                    ? Uri.UriSchemeHttps + url2.Substring(Uri.UriSchemeHttp.Length)
                    : Uri.UriSchemeHttp + url2.Substring(Uri.UriSchemeHttps.Length);
                LookupPage2(webpage, url, url2, changeUrl: false);
            }
        }

        private static void LookupPage2(WebPage webpage, string url, string url2, bool changeUrl = false)
        {
            if (url == url2)
            {
                return;
            }
            var page2 = ctx.WebPages.FirstOrDefault(wp => wp.Url == url2);
            if (page2 == null)
            {
                if (changeUrl)
                {
                    webpage.Url = url2;
                    ctx.SaveChanges();
                }
            }
            else
            {
                var df1 = webpage.DraftFilespec;
                var df2 = page2.DraftFilespec;
                var f1 = webpage.Filespec;
                var f2 = page2.Filespec;

                Console.WriteLine($"{url}[{f1}]\t{url2}[{f2}]");
                if (df1 != null && df2 != null && !df1.Equals(df2, StringComparison.InvariantCultureIgnoreCase))
                {
                    Console.WriteLine($"{df1}\t{df2}");
                }
                if (f1 != null && f2 != null && !f1.Equals(f2, StringComparison.InvariantCultureIgnoreCase))
                {
                    Console.WriteLine($"{f1}\t{f2}");
                    webpage.Filespec = page2.Filespec;
                }
                else
                {
                    if (page2.Filespec == null && webpage.Filespec != null)
                    {
                        Console.WriteLine($"{f1}\t{f2}");
                        page2.Filespec = webpage.Filespec;
                    }
                }
            }
        }

        static string StdUrl(string url)
        {
            var builder = new UriBuilder(url);
            //if (builder.Path.EndsWith("/"))
            //{
            //    builder.Path = builder.Path.Substring(0, builder.Path.Length - 1);
            //}
            if (builder.Query == "?")
            {
                builder.Query = "";
            }
            var url2 = Infrastructure.Utils.NoTrailSlash(builder.Uri.AbsoluteUri);
            if (url != url2)
            {
                Console.WriteLine($"{url}\t=>\t{url2}");
            }
            return url2;
        }
    }
}
