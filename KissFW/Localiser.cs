using System;
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
            var usingfs = webpage.Filespec;
            if (webpage.Download != WebPage.DownloadEnum.Downloaded         // not fully downloaded ?
               || !File.Exists(usingfs))                                    // or file [now] missing
            {
                if (!getMissing                                             // or download impossible
                //    || webpage.Download != WebPage.DownloadEnum.Ignore      // or invalid state
                    || (!(await Downloader.FetchFileAsync(webpage)))        // try [another] download. did it fail?
                    || webpage.Download != WebPage.DownloadEnum.Downloaded) // or not refreshed to fully downloaded
                {
                    return false;                                           // can't localise
                }
            }

            IDictionary<string, string> mydict = new SortedDictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);   // Dictionary<string, string>()
            foreach (var dad in webpage.ConsumeFrom)
            {
                var supplied = dad;
                while (supplied?.Download == WebPage.DownloadEnum.Redirected || supplied?.Download == WebPage.DownloadEnum.Ignore)
                {
                    if (supplied?.Download == WebPage.DownloadEnum.Redirected)
                    {
                        supplied = supplied.ConsumeFrom.FirstOrDefault();           // redirected should have exactly ONE redirect, but redirect may cascade anew
                    }
                    else
                    {
                        if (!getMissing                                             // can we do last-chance download? ...
                            || supplied?.Download != WebPage.DownloadEnum.Ignore    // in appropriate state ?
                            || supplied.Filespec.StartsWith(ERRTAG)                 //  and request not already rejected
                            || !(await Downloader.FetchFileAsync(supplied)))        // try [another] download. did it fail ?
                        {
                            break;                                                  // quit while loop [d/l success needs re-test for redirect so loop again]
                        }
                    }
                }
                string usedfs;
                if (supplied.Download.Value == WebPage.DownloadEnum.Downloaded
                    && !string.IsNullOrWhiteSpace(usedfs = supplied.Filespec)
                    && !usedfs.StartsWith(ERRTAG)
                    && File.Exists(usedfs))
                {
                    // make usedfs relative to usingfs
                    var relfs = Utils.GetRelativePath(usingfs, usedfs);

                    if (!mydict.ContainsKey(supplied.Url))                        // protect against redirections feeding duplicates
                    {
                        mydict.Add(supplied.Url, relfs);                            //
                        if (mydict.Count > maxlinks)
                        {
                            break;                                                  // now at capacity, so exit the foreach
                        }
                    }
                }
            }
            var thisUrl = webpage.Url;
            if (!mydict.ContainsKey(thisUrl))                                       // relativise link-to-self (including any fragment)
            {
                mydict.Add(thisUrl, ".");                                           // Path.GetFileName(webpage.Filespec)
            }

            Httpserver.LoadFromFile(webpage.Url, usingfs);
            var changedLinks = Httpserver.ReworkLinks(usingfs, mydict);
            if (!changedLinks)
            {
                return false;        // no link replacement achieved
            }

            var newFilespec =       // htmldir + Path.DirectorySeparatorChar + Path.GetRandomFileName()
                Path.Combine(HtmlPath, Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".html");
            Httpserver.SaveFile(newFilespec);

            var backfs = Path.Combine(BackupPath, Path.GetFileName(usingfs));
            var result = Utils.RetireFile(usingfs, backfs, newFilespec);

            return result;
        }
    }
}
