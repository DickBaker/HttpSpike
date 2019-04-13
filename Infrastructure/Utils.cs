﻿using System;
using System.Collections.Generic;
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

        public static (string filename, string extn) FileExtSplit(string instr)
        {
            string fname = null, extn = null;
            var proto = MakeValid(instr);       // will remove any trailing "/". finally does .Trim() but not TrimOrNull()
            if (!string.IsNullOrWhiteSpace(proto))
            {
                //#pragma warning disable CA1307 // Specify StringComparison
                //                if (proto[proto.Length - 1] == '/')             // proto.EndsWith("/")
                //#pragma warning restore CA1307 // Specify StringComparison
                //                {
                //                    proto = proto.Substring(0, proto.Length - 1).TrimEnd();
                //                }
                fname = Path.GetFileNameWithoutExtension(proto);
                if (!string.IsNullOrWhiteSpace(fname))          // MUST be a filename (otherwise caller _may_ substitute "unknown")
                {
                    extn = Path.GetExtension(proto);
                    if (extn.Length > 0 && extn[0] == '.')
                    {
                        extn = extn.Substring(1);
                    }
                    return (MimeCollection.IsValidExtn(extn))   // ANY match ?
                    ? (fname, extn)                             // yes. pass extn as-is
                    : (fname, null);                            // no. makes no guesses (content/type will prevail later)
                }
            }
            return (null, null);
        }

        public static string FileExtnFix(string instr)
        {
            string filename, extn;
            (filename, extn) = FileExtSplit(instr);
            return (filename == null && extn == null)
                ? null
                : (filename ?? "unknown") + ((extn == null) ? "" : EXTN_SEPARATOR + extn);
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

        public static string NoTrailSlash(string value)
        {
            var _url = value.Trim();
            return _url.EndsWith("/")
            ? _url.Substring(0, _url.Length - 1).TrimEnd()        // standardise to strip any trailing "/"
            : _url;
        }

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