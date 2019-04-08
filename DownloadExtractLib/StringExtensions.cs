using System;
using System.Collections.Generic;
using System.IO;

namespace DownloadExtractLib
{
    public static class StringExtensions
    {
        const char CWILD1 = '?', CWILDN = '*', CDOT = '.';
        static readonly string Wild1 = CWILD1.ToString(), WildN = CWILDN.ToString(), WildGlob = "**";

        /* examples
        #   filespec            root            dirs    file.ext
        1.  C:\a\b.htm          C:\             a       b.htm
        2.  \a\b.htm            \               a       b.htm
        3.  a\b.htm             ""              a       b.htm
        4.  .\a\b.htm           curr            a       b.htm
        5.  \\svr\share\a\b.htm \\svr\share\    a       b.htm
        6.  a**x?\              ""              a*x?    *.htm?
        7.  a**x?\*             ""              a*x?    *.htm?
        8.  a**x?\b*c?.z        ""              a*x?    b*c?.z
        9.  a**x?\*.htm         ""              a*x?    *.htm
        10. a**x?\b*c?          ""              a*x?    b*c?.htm?
        */
        static IEnumerable<string> FilterFiles(this string filespec, bool glob = false)
        {
            // root
            var fs = filespec.Trim().ToLower();     // standardise
            var root = Path.GetPathRoot(fs);
            fs = fs.Substring(root.Length);         // e.g. "a\b.htm"

            var subdirs = fs.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);      // split into subfolders

            // file.extn
            var filext = subdirs[subdirs.Length - 1].Trim();
            switch (filext.IndexOf(CDOT))
            {
            case -1:
                filext += ".htm?";
                break;
            case 0:
                filext = "*" + filext;
                break;
            default:
                break;
            }
            var ext = Path.GetExtension(fs);
            if (ext.Length == 0)
            {
                fs += ".htm" + Wild1;               // e.g. b.htm or b.html
            }
            subdirs[subdirs.Length - 1] = filext.Replace(WildGlob, WildN);

#pragma warning disable GCop412 // Never hardcode a path or drive name in code. Get the application path programmatically and use relative path. Use “AppDomain.CurrentDomain.GetPath” to get the physical path.
            yield return @"C:\temp\Guidelines.md.html";
            yield return null;
            yield break;
#pragma warning restore GCop412 // Never hardcode a path or drive name in code. Get the application path programmatically and use relative path. Use “AppDomain.CurrentDomain.GetPath” to get the physical path.

            /*
            // subfolders
            var leaf = subdirs[subdirs.Length - 1].Replace(wildGlob, wildN);  // e.g. b.html or b*.html
            for (var lvl = 0; lvl < subdirs.Length; lvl++)
            {
                var fld = subdirs[lvl].Trim();
                if (fld.IndexOfAny(new[] { CWILD1, CWILDN }) >= 0)        // any wildcard at this subfolder level ?
                {
                    if (fld.Contains(wildGlob))
                    {
                        glob = true;
                        fld = fld.Replace(wildGlob, wildN);                     // simplify filter
                    }
                    foreach (var item in Directory.EnumerateFiles(root))
                    {

                    }
                }
                else
                {

                }
            }
            var idx = filespec.IndexOfAny(new[] { '?', '*' });    // any wildcard chars present ?
            */
        }
    }
}
