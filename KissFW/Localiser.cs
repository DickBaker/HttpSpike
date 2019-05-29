using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DownloadLib;
using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;

namespace KissFW
{
    public class Localiser
    {
        const string ERRTAG = "~";

        readonly IHttpParser Httpserver;
        readonly Downloader Downloader;
        readonly string HtmlPath;               // subfolder to read *.html
        readonly string BackupPath;             //  ditto to write revised (localised) *.html
        public Localiser(IHttpParser httpserver, string htmlPath, string backupPath = null, Downloader download = null)
        {
            Httpserver = httpserver;
            Downloader = download;
            HtmlPath = htmlPath;
            BackupPath = backupPath;
        }

        public async Task<bool> Translate(WebPage webpage, int maxlinks, bool getMissing = false)
        {
            if (Downloader == null)
            {
                getMissing = false;
            }
            var origfs = webpage.Filespec;
            if (webpage.Download != WebPage.DownloadEnum.Downloaded         // not fully downloaded ?
                || !File.Exists(origfs)                                     // or file [now] missing
                || !getMissing                                              // or download impossible
                || webpage.Download != WebPage.DownloadEnum.Ignore          // or invalid state
                || !(await Downloader.FetchFileAsync(webpage))              // or [another] download attempt failed
                || webpage.Download != WebPage.DownloadEnum.Downloaded)     // or not refreshed to fully downloaded
            {
                return false;                                               // can't localise
            }

            var mydict = new Dictionary<string, string>();
            foreach (var dad in webpage.ConsumeFrom)
            {
                var supplied = dad;
                while (supplied?.Download == WebPage.DownloadEnum.Redirected || supplied?.Download == WebPage.DownloadEnum.Ignore)
                {
                    if (supplied?.Download == WebPage.DownloadEnum.Redirected)
                    {
                        supplied = supplied.ConsumeFrom.FirstOrDefault();   // redirected should have exactly ONE redirect, but redirect may cascade anew
                    }
                    else
                    {
                        if (!getMissing                                             // can we do last-chance download? ...
                            || supplied?.Download != WebPage.DownloadEnum.Ignore    // in appropriate state ?
                            || !(await Downloader.FetchFileAsync(supplied)))        // but did [another] download fail ?
                        {
                            break;                                                  // quit while loop [d/l success needs re-test for redirect so loop again]
                        }
                    }
                }
                string fs;
                if (supplied.Download.Value == WebPage.DownloadEnum.Downloaded
                    && !string.IsNullOrWhiteSpace(fs = supplied.Filespec)
                    && !fs.StartsWith(ERRTAG)
                    //&& !mydict.ContainsKey(supplied.Url)                          // should be unnecessary as WebPage.Url is unique
                    && File.Exists(fs))
                {
                    mydict.Add(supplied.Url, fs);
                    if (mydict.Count > maxlinks)
                    {
                        break;                                                      // now at capacity, so exit the foreach
                    }
                }
            }
            if (mydict.Count==0)
            {
                return false;                                                       // no links to replace
            }

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
