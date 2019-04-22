using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Infrastructure.Interfaces;
using Infrastructure.Models;

namespace KissFW
{
    public class Localiser
    {

        readonly IRepository Dataserver;
        readonly IHttpParser Httpserver;
        readonly string HtmlPath;               // subfolder to read *.html
        readonly string LocalPath;              //  ditto to write revised (localised) *.html
        public Localiser(IRepository dataserver, IHttpParser httpserver, string htmlPath, string localPath)
        {
            Dataserver = dataserver;
            Httpserver = httpserver;
            HtmlPath = htmlPath;
            LocalPath = localPath;
        }

        internal Task Translate(WebPage webpage)
        {
            var mydict = new Dictionary<string, string>();
            foreach (var dad in webpage.ConsumeFrom)
            {
                var fs = dad.Filespec;
                if (!string.IsNullOrWhiteSpace(fs))
                {
                    mydict.Add(dad.Url, fs);
                }
            }
            Httpserver.LoadFromFile(webpage.Url, webpage.Filespec);
            Httpserver.ReworkLinks(webpage.Url, webpage.Filespec, mydict);
            return Task.FromResult<bool>(true);
        }
    }

    public class LocalDto
    {
        public string Url { get; set; }

        public string Filespec { get; set; }

    }
}
