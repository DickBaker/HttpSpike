using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Infrastructure.Models;

namespace Infrastructure
{
    public static class Utils
    {
        static readonly char[] BadChars = Path.GetInvalidFileNameChars();
        const string EXTN_SEPARATOR = ".";
        //static readonly char[] DIRSEP = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        public static (string filename, string extn) FileExtSplit(string instr)
        {
            var proto = MakeValid(instr);                       // will remove any trailing "/". finally does .Trim() but not TrimOrNull()
            if (!string.IsNullOrWhiteSpace(proto))
            {
                var fname = Path.GetFileNameWithoutExtension(proto);
                if (!string.IsNullOrWhiteSpace(fname))          // MUST be a filename
                {
                    var extn = Path.GetExtension(proto);
                    if (extn.Length > 0 && extn[0] == '.')
                    {
                        extn = extn.Substring(1);
                    }
                    return (MimeCollection.IsValidExtn(extn))   // ANY match ?
                        ? (fname, extn)                         // yes. pass extn as-is
                        : (proto, null);                        // no. makes no guesses (content/type will prevail later)
                }
            }
            return (null, null);
        }

        public static string FileExtnFix(string instr)
        {
            string filename, extn;
            (filename, extn) = FileExtSplit(instr);
            return (filename == null)
                ? null
                : filename + ((extn == null) ? "" : EXTN_SEPARATOR + extn);
        }

        public static string GetRelativePath(string fromPath, string toPath)
        {
            //return topath;                // TODO: [write later] if CORE use Path.GetRelativePath, else cf https://stackoverflow.com/questions/275689/how-to-get-relative-path-from-absolute-path/1599260#1599260

            #region StackOverflow solution

            if (string.IsNullOrEmpty(fromPath))
            {
                throw new ArgumentNullException(nameof(fromPath));
            }

            if (string.IsNullOrEmpty(toPath))
            {
                throw new ArgumentNullException(nameof(toPath));
            }

            var fromUri = new Uri(fromPath);
            var toUri = new Uri(toPath);

            if (fromUri.Scheme != toUri.Scheme) { return toPath; } // path can't be made relative.

            var relativePath = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString());

            //if (toUri.Scheme.Equals("file", StringComparison.InvariantCultureIgnoreCase))
            //{
            //    relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            //}
            #endregion

            #region Dick (commented-out)
            /*
            Debug.Assert(fromPath == Path.GetFullPath(fromPath), "fromPath not normalised");
            Debug.Assert(toPath == Path.GetFullPath(toPath), "toPath not normalised");
            var subdirs = fromPath.Split(DIRSEP, StringSplitOptions.RemoveEmptyEntries);
            var subdirs2 = toPath.Split(DIRSEP, StringSplitOptions.RemoveEmptyEntries);
            if (subdirs[0] != subdirs2[0])
            {
                return toPath;
            }
            var sb = new StringBuilder();
            for (var i = 1; i < subdirs2.Length - 1; i++)
            {
                if (sb.Length > 0 || (i >= subdirs.Length - 1))
                {
                    sb.Append(subdirs2[i] + Path.DirectorySeparatorChar);
                }
                else
                if (subdirs[i] != subdirs2[i])
                {
                    sb.Append(".." + Path.DirectorySeparatorChar);
                }
            }
            sb.Append(subdirs2[subdirs2.Length - 1]);
            var rslt = sb.ToString();

            Debug.Assert(relativePath == rslt, "SO and Dick solution disagree!");
            */
            #endregion

            return relativePath;

        }

        /// <summary>
        ///     replace any characters within param by space, but elliding any multiple spaces
        /// </summary>
        /// <param name="rawstr">
        ///     candidate filespec (possibly from webpage title)
        /// </param>
        /// <returns>
        ///     either valid filespec or null
        /// </returns>
        /// <remarks>
        ///     avoids any string.Trim() that would involve string alloc+copy (and GC L0)
        /// </remarks>
        public static string MakeValid(string rawstr)
        {
            if (string.IsNullOrWhiteSpace(rawstr))
            {
                return null;
            }
            var sb = new StringBuilder(rawstr.Length);      // assume capacity for every character
            for (var i = 0; i < rawstr.Length; i++)
            {
                var c = rawstr[i];
                if (c == '\r' || c == '\n')
                {
                    break;                                  // only accept the first line
                }
                if (char.IsWhiteSpace(c))
                {
                    if (sb.Length == 0 || char.IsWhiteSpace(sb[sb.Length - 1]))
                    {
                        continue;                           // suppress any leading whitespace [i.e.TrimStart] or secondary whitespace
                    }
                }
                if (BadChars.Contains(c))
                {
                    if (sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1])) // ignore any illegal chars where previous char was whitespace
                    {
                        sb.Append(' ');                         // otherwise insert single space instead of the bad char
                    }
                    continue;
                }
                sb.Append(c);                                   // append valid char (N.B. may be whitespace)
            }
            if (sb.Length == 0)
            {
                return null;
            }
            return char.IsWhiteSpace(sb[sb.Length - 1])         // final char is whitespace ?
                ? sb.ToString(0, sb.Length - 1)                 // yes. remove final whitespace, i.e. acts like TrimEnd()
                : sb.ToString();                                // no. pass the whole builder content
        }

        // eliminate any fragment, but don't standardise e.g. to lowercase (caller should employ StringComparison.InvariantCultureIgnoreCase)
        public static Uri NoFragment(string url) =>
            new Uri(new Uri(url, UriKind.RelativeOrAbsolute)
                .GetLeftPart(UriPartial.Query), UriKind.RelativeOrAbsolute);

        /*
        public static string NoTrailSlash(string value)         // *** UNUSED ***
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                var idx = value.Length;
                while (--idx >= 0)
                {
                    var c = value[idx];
                    if (c != '/' && !char.IsSeparator(c)                // trailing "/" or blank [i.e. TrimEnd()]
                        && (c != '?' || idx != value.IndexOf('?')))     // remove trailing queryparam delimiter (i.e.first and only"?")
                    {
                        return value.Substring(0, idx + 1);
                    }
                }
            }
            return null;
        }
        */

        /*
        public static (string extn, bool isString) ParseType(string contentType)
        {
            switch (contentType)
            {
                case "application/font-woff":
                    return (".woff", false);
                case "application/octet-stream":        // RFC2616 7.2.1 says this should be default guess
                    return (".bin", false);
                case "application/x-shockwave-flash":       // **
                    return (".swf", false);
                case "audio/ogg":
                case "video/ogg":
                    return (".ogg", false);
                case "audio/mpeg":
                    return (".mp3", false);
                case "audio/wav":
                    return (".wav", false);
                case "image/gif":
                    return (".gif", false);
                case "image/png":
                    return (".png", false);
                case "image/webp":
                    return (".webp", false);
                case "text/css":
                    return (".css", true);
                case "text/html":
                    return (".html", true);
                case "application/javascript":
                case "application/x-javascript":
                case "text/javascript":
                    return (".js", true);
                case "video/mp4":
                    return (".mp4", false);
                case "text/plain":
                default:
                    return (".txt", true);
            }
        }
        */

        public static string RandomFilenameOnly() =>
            Path.GetFileNameWithoutExtension(                   // this produces a file5678 format
                Path.GetRandomFileName());                      //  from original file5678.ext4 format

        public static bool RetireFile(string filespec, string backupfilespec = null, string replacefilespec = null)
        {
            if (string.IsNullOrWhiteSpace(filespec)
                || (backupfilespec == null && replacefilespec == null)
                || filespec == backupfilespec
                || filespec == replacefilespec
                || backupfilespec == replacefilespec)
            {
                throw new InvalidOperationException($"RetireFile: invalid params({filespec}, {backupfilespec}, {replacefilespec})");
            }
            try
            {
                if (File.Exists(filespec))
                {
                    if (backupfilespec == null)
                    {
                        File.Delete(filespec);
                    }
                    else
                    {
                        if (File.Exists(backupfilespec))
                        {
                            File.Delete(backupfilespec);
                        }
                        File.Move(filespec, backupfilespec);
                    }
                }
            }
            catch (Exception e1)
            {
                throw new InvalidOperationException($"RetireFile: failed to delete/move {filespec}\n\t{e1.Message}");
            }

            try
            {
                if (replacefilespec != null && File.Exists(replacefilespec))
                {
                    File.Move(replacefilespec, filespec);
                }
                return true;
            }
            catch (Exception e2)         // replaceFile still as-was (hopefully!)
            {
                throw new InvalidOperationException($"RetireFile: failed to move {replacefilespec}\n\t{e2.Message}");
            }
        }

        // extension method to simplify common requirement
        public static string TrimOrNull(string raw) =>
            raw == null || string.IsNullOrWhiteSpace(raw)
                ? null
                : raw.Trim();                   // removes leading/trailing whitespace (incl CR/LF)

        public static void BombIf(this Task t)
        {
            if (t.IsFaulted)
            {
                throw new Exception($"task failed with exception {t.Exception}");
            }
        }

        public static void WaitBombIf(this Task t)
        {
            try
            {
                t.Wait();
                t.BombIf();
            }
            catch (Exception excp)
            {
                Console.WriteLine(excp);
            }
        }
    }
}
