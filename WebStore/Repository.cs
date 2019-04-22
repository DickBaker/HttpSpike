using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Interfaces;
using Infrastructure.Models;

namespace WebStore
{
    public class Repository : IRepository
    {
        readonly Webstore.WebModel EfDomain;

        public Repository(Webstore.WebModel dbctx)
        {
            EfDomain = dbctx;
            //  var ObjCtx = (EfDomain as IObjectContextAdapter).ObjectContext;
            //  ObjCtx.SavingChanges += OnSavingChanges;
        }

        public async Task AddLinksAsync(WebPage webpage, IDictionary<string, string> linksDict)
        {
            //var frig = 0.8d;
            //var initSize = (int)Math.Ceiling(linksDict.Count * frig);
            //var anoUrls = new List<string>(initSize);
            //var anoPages = new List<WebPage>(initSize);
            foreach (var kvp in linksDict)                         // .OrderBy(lnk => lnk.Key)
            {
                var linkedUrl = kvp.Key;                           // Utils.NoTrailSlash()
                var linkfilespec = kvp.Value;                      // Utils.MakeValid()
                Console.WriteLine($"\t[{linkedUrl}]\t:=\t{linkfilespec}");
                //var wpneeded = Dataserver.PutWebPage(new WebPage(linkedUrl, linkfilespec));
                //var wpneeded = new WebPage(linkedUrl, linkfilespec);
                var wptemp =
                    webpage.ConsumeFrom.FirstOrDefault(wp => wp.Url.Equals(linkedUrl, StringComparison.InvariantCultureIgnoreCase)) ??  // already exists [at dependent] ?
                //  EfDomain.WebPages.Local.FirstOrDefault(wp => wp.Url.Equals(linkedUrl, StringComparison.InvariantCultureIgnoreCase));    // already exists [Local only] ?
                    await EfDomain.WebPages.FirstOrDefaultAsync(wp => wp.Url.Equals(linkedUrl, StringComparison.InvariantCultureIgnoreCase));          // already exists [at db] ?
                if (wptemp == null)
                {
                    /*
                    wptemp = PutWebPage(new WebPage(linkedUrl, linkfilespec));        // check repository [will upsert DraftFilespec & Filespec]
                    if (webpage.ConsumeFrom.Contains(wptemp))
                    {
                        Console.WriteLine($"{webpage.Url}({webpage.PageId}) ConsumeFrom actually contains {wptemp.Url}");
                    }
                    else
                    {
                        webpage.ConsumeFrom.Add(wptemp);
                    }
                    */
                    wptemp = new WebPage(linkedUrl, linkfilespec);
                    webpage.ConsumeFrom.Add(wptemp);
                }
                else
                {
                    var draftFilespec = wptemp.DraftFilespec;
                    if (!draftFilespec.Equals(linkfilespec, StringComparison.InvariantCultureIgnoreCase) && (linkfilespec != null))
                    {
                        if (draftFilespec == null)
                        {
                            Console.WriteLine($"PutHost[DraftFilespec] {draftFilespec} -> {linkfilespec}");
                        }
                        else
                        {
                            Console.WriteLine($"PutHost[DraftFilespec] {wptemp.DraftFilespec} => {linkfilespec}");
                        }
                        wptemp.DraftFilespec = linkfilespec;
                    }
                }
            }
        }

        /*
        void OnSavingChanges(object sender, EventArgs e)
        {
            if (!(sender is ObjectContext ObjCtx))
            {
                return;
            }
            WebPageChanging(ObjCtx, EntityState.Deleted, "deleting");
            WebPageChanging(ObjCtx, EntityState.Added, "adding");
            WebPageChanging(ObjCtx, EntityState.Modified, "updating");
        }

        static void WebPageChanging(ObjectContext ObjCtx, EntityState changeType, string action)
        {
            Console.WriteLine($"{action}");
            foreach (var stateitem in ObjCtx.ObjectStateManager.GetObjectStateEntries(changeType))
            {
                if (!(stateitem.Entity is WebPage webpage))
                {
                    continue;
                }
                Console.WriteLine($"\t{webpage.Url}({webpage.PageId})");
            }
        }
        */

        /// <summary>
        ///     return all known distinct ContentTypeToExtn entities
        /// </summary>
        /// <returns>
        ///     enumerable of ContentTypeToExtn entities sorted by ContentTypeToExtn.Template
        /// </returns>
        public Task<List<ContentTypeToExtn>> GetContentTypeToExtnsAsync() =>
            EfDomain.ContentTypeToExtns
            .Where(row => !string.IsNullOrEmpty(row.Template) && !string.IsNullOrEmpty(row.Extn))   // WHERE ((LEN([Extent1].[Template])) <> 0) AND ((LEN([Extent1].[Extn])) <> 0)
            .OrderBy(row => row.Template)
            .Distinct()
            .ToListAsync();

        //public WebPage GetWebPageById(int id) => EfDomain.WebPages.FirstOrDefault(row => row.PageId == id);
        //public WebPage GetWebPageByUrl(string url) => EfDomain.WebPages.FirstOrDefault(row => row.Url == url);
        //public IEnumerable<WebPage> GetWebPages() => EfDomain.WebPages;

        /*
        public IEnumerable<WebPage> GetWebPagesToDownload()
        {
            var hosts2 = EfDomain.WebPages
                        .Where(w1 => w1.NeedDownload.Value && w1.Filespec == null)      // not already downloaded
                                                                                        //.Where(w1 => w1.NeedDownload.Value && w1.Filespec != null)      //  already downloaded
                        .Where(w1 =>
                                   w1.Url.Contains("5minutesinchurchhistory.com")
                                || w1.Url.Contains("ligonier")
                                || w1.Url.Contains("reformationbiblecollege.com")
                                || w1.Url.Contains("reformationstudybible.com")
                                || w1.Url.Contains("renewingyourmind.org")
                                || w1.Url.Contains("thestateoftheology.com")
                                || w1.Url.Contains("reformationtrust.com")
                                || w1.Url.Contains("soteriology101")
                                || w1.Url.Contains("tabletalkmagazine.com")
                              )
                        //.Where(w1 => !w1.DraftFilespec.EndsWith(".html") && !w1.DraftFilespec.EndsWith(".aspx"))
                        .GroupBy(w2 => w2.HostId)                                       // although we have no Host entity, can use to find populist Hosts
                        .OrderByDescending(g1 => g1.Count())
                        .ThenBy(g1 => g1.Key)
                        .Select(g2 => g2)
                        .ToList();
            foreach (var g3 in hosts2)
            {
                Console.WriteLine($"count={g3.Count()} for HostId={g3.Key}");
                foreach (var wp in g3)
                {
                    Console.WriteLine(wp.Url);
                    yield return wp;
                }
            }
        }
        */

        public Task<List<WebPage>> GetWebPagesToDownloadAsync(int maxrows = 20)
        {
            var wantedIds = EfDomain.WebPages
                    .SqlQuery("exec p_ToDownload @Take=@TakeN", new SqlParameter("@TakeN", SqlDbType.Int) { Value = maxrows })   // DbSqlQuery<WebPage>
                    .Select(wp => wp.PageId)
                    .ToList();                                  // solidify as List<int> (i.e. no deferred execution)
            var wanteds2 = EfDomain.WebPages
                            .Include("ConsumeFrom")             // acquire all pages that this page is [already] known to reference
                            .Where(wp => wantedIds.Contains(wp.PageId))
                            .Select(wp => wp)
                            .ToListAsync();
            return wanteds2;
        }

        public Task<List<WebPage>> GetWebPagesToLocaliseAsync(int maxrows = 15) => throw new NotImplementedException();

        /*
        WebPage PutWebPage(WebPage webpage)
        {
            if (EfDomain.WebPages.Local.Count == 0)             // read entire table on first call
            {
                var webPages = EfDomain.WebPages.ToList();
                Console.WriteLine($"pagecnt={EfDomain.WebPages.Local.Count}");
            }

            // try local cache before external trip to DB
            var wptemp =
                //EfDomain.WebPages.Local.FirstOrDefault(row => row.Url.Equals(webpage.Url, StringComparison.InvariantCultureIgnoreCase))
                EfDomain.WebPages.FirstOrDefault(row => row.Url.Equals(webpage.Url, StringComparison.InvariantCultureIgnoreCase));
            if (wptemp == null)
            {
                return EfDomain.WebPages.Add(webpage);
            }
            if (!webpage.DraftFilespec.Equals(wptemp.DraftFilespec, StringComparison.InvariantCultureIgnoreCase))
            {
                if (wptemp.DraftFilespec == null)
                {
                    Console.WriteLine($"PutHost[DraftFilespec] {wptemp.DraftFilespec} -> {webpage.Filespec}");
                    wptemp.DraftFilespec = webpage.DraftFilespec;
                }
                else
                {
                    Console.WriteLine($"===> check {wptemp.DraftFilespec} -> {webpage.DraftFilespec}");
                }
            }
            if (webpage.Filespec != null && wptemp.Filespec != webpage.Filespec)
            {
                Console.WriteLine($"PutHost[Filespec] {wptemp.Filespec} -> {webpage.Filespec}");
                wptemp.Filespec = webpage.Filespec;
            }
            return wptemp;
        }
        */

        public Task<int> SaveChangesAsync() => EfDomain.SaveChangesAsync();


        public int SaveChanges() => EfDomain.SaveChanges();
    }
}
