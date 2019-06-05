namespace DataHosts.Migrations
{
    using System.Data.Entity.Migrations;
    using Infrastructure.Models;

    internal sealed class Configuration : DbMigrationsConfiguration<WebModel>
    {
        public Configuration() => AutomaticMigrationsEnabled = false;

        protected override void Seed(WebModel context)
        {
            //  This method will be called after migrating to the latest version.

            //  You can use the DbSet<T>.AddOrUpdate() helper extension method 
            //  to avoid creating duplicate seed data. E.g.
            //
            //    context.People.AddOrUpdate(
            //      p => p.FullName,
            //      new Person { FullName = "Andrew Peters" },
            //      new Person { FullName = "Brice Lambson" },
            //      new Person { FullName = "Rowan Miller" }
            //    );
            //
            
            // these were found in real-world but not in official docs
            context.ContentTypeToExtns.AddOrUpdate(p => p.Template, 
                new ContentTypeToExtn( "text/xml+oembed", "json", true),
                new ContentTypeToExtn( "application/x-javascript", "js", true),
                new ContentTypeToExtn(  "application/rss+xml", "xml", true));

            context.WebPages.AddOrUpdate(w => w.Url,
                new WebPage("http://www.ligonier.org/blog/rc-sprouls-crucial-questions-ebooks-now-free", "rc-sprouls-crucial-questions-ebooks-now-free.html", @"C:\Ligonier\webcache\hh2z3ipo.html", WebPage.DownloadEnum.Downloaded, WebPage.LocaliseEnum.ToLocalise),
                new WebPage("http://www.ligonier.org/learn/teachers/john-piper", "john-piper.html", null, WebPage.DownloadEnum.Redirected, WebPage.LocaliseEnum.Ignore) // watch NULL !
                );
        }
    }
}