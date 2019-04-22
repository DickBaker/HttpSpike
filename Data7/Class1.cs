using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Data7
{
    public class Class1
    {
        Model1 ctx;
        public Class1()
        {
            ctx = new Model1();
            var templates = ctx.ContentTypeToExtns.ToList();
            var wp = ctx.WebPages.Where(w => w.PageId == 23700).FirstOrDefault();
            Console.WriteLine($"{wp.SupplyTo.Count}, {wp.ConsumeFrom.Count}");

            var wp2 = ctx.WebPages.Include("ConsumeFrom").Include("SupplyTo").Where(w => w.PageId == 23700).FirstOrDefault();
            Console.WriteLine($"{wp2.SupplyTo.Count}, {wp2.ConsumeFrom.Count}");

            var anoprm = new SqlParameter("@TakeN", SqlDbType.Int)  // have to recreate every time (presumably as EF invents new SqlCommand) to avoid
            { Value = 13 };                                    //  "The SqlParameter is already contained by another SqlParameterCollection" error
            var pages = ctx.WebPages
                .SqlQuery("exec dbo.p_ToDownload @Take=@TakeN", anoprm)
                .ToList();                                 // solidify as List<WebPage> (i.e. no deferred execution), and caller will await to get # requested
            Console.WriteLine($"{pages.Count}");
        }

        public async Task<WebPage[]> GetWorkAsync(int cnt)
        {
            var anoprm = new SqlParameter("@TakeN", SqlDbType.Int)  // have to recreate every time (presumably as EF invents new SqlCommand) to avoid
            { Value = cnt };                                    //  "The SqlParameter is already contained by another SqlParameterCollection" error
            var pages = await ctx.WebPages
                .SqlQuery("exec dbo.p_ToDownload @Take=@TakeN", anoprm)
                .ToArrayAsync();                                 // solidify as List<WebPage> (i.e. no deferred execution), and caller will await to get # requested
            return pages;
        }
    }
}
