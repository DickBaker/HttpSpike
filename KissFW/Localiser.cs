using System.Collections.Generic;
using System.IO;
using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;

namespace KissFW
{
    public class Localiser
    {
        const string ERRTAG = "~";

        readonly IHttpParser Httpserver;
        readonly string HtmlPath;               // subfolder to read *.html
        readonly string BackupPath;             //  ditto to write revised (localised) *.html
        public Localiser(IHttpParser httpserver, string htmlPath, string backupPath = null)
        {
            Httpserver = httpserver;
            HtmlPath = htmlPath;
            BackupPath = backupPath;
        }

        public bool Translate(WebPage webpage, int maxlinks)
        {

            var mydict = new Dictionary<string, string>();
            foreach (var dad in webpage.ConsumeFrom)
            {
                var fs = dad.Filespec;
                if (dad.Download.Value == WebPage.DownloadEnum.Downloaded && !string.IsNullOrWhiteSpace(fs) && !fs.StartsWith(ERRTAG))
                {
                    mydict.Add(dad.Url, fs);
                    if (mydict.Count > maxlinks)
                    {
                        break;
                    }
                }
            }
            var origfs = webpage.Filespec;
            Httpserver.LoadFromFile(webpage.Url, origfs);
            var changedLinks = Httpserver.ReworkLinks(origfs, mydict);
            if (!changedLinks)
            {
                return false;        // no link replacement achieved
            }

            var newFilespec =       // htmldir + Path.DirectorySeparatorChar + Path.GetRandomFileName()
                Path.Combine(HtmlPath, Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".html");
            Httpserver.SaveFile(newFilespec);

            var backfs = Path.Combine(BackupPath, Path.GetFileName(origfs));
            var result = Utils.RetireFile(origfs, backfs, newFilespec);

            return result;
        }
    }
}
