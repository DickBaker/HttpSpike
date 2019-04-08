using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Infrastructure
{
    public static class Utils
    {
        static readonly char[] CRLF = { '\r', '\n' };
        static readonly char[] BadChars = Path.GetInvalidFileNameChars();

        // TODO: or use SortedList<TKey,TValue>() ??
        public static Dictionary<string, string> MimeToExtn { get; } = new Dictionary<string, string>();
        // MimeToExtn has 637 entries, whereas distinct Extns number only 78 so more efficient search
        // cf. SortedList or Hash or HashSet
        static readonly Lazy<HashSet<string>> ValidExtns = new Lazy<HashSet<string>>(() =>
        {
            //return new HashSet<string>(MimeToExtn.Values.Distinct());
            //var grp = MimeToExtn.Values
            //.GroupBy(extn => extn)
            //.OrderByDescending(g => g.Count())
            //.ThenBy(g => g.Key)
            //.Select(g => g.Key);
            return new HashSet<string>(
                MimeToExtn.Values
                .GroupBy(extn => extn.ToLowerInvariant())   // rogue "XML" present in data
                .OrderByDescending(g => g.Count())          // favour popular extn's first (e.g. xml)
                .ThenBy(g => g.Key)                         //  then aphabetic
                .Select(g => g.Key)
                );
        });

        public static (string filename, string extn) FileExtLastSegment(string instr)
        {
            if (!string.IsNullOrWhiteSpace(instr))
            {
                var lastSegment = instr.Trim().ToLowerInvariant();
#pragma warning disable CA1307 // Specify StringComparison
                if (lastSegment.EndsWith("/"))
#pragma warning restore CA1307 // Specify StringComparison
                {
                    lastSegment = lastSegment.Substring(0, lastSegment.Length - 1).TrimEnd();
                }
                if (lastSegment.Length > 0)
                {
#pragma warning disable CA1307 // Specify StringComparison
                    var dlim = lastSegment.LastIndexOf(".");
#pragma warning restore CA1307 // Specify StringComparison
                    if (dlim < 0)
                    {
                        return (lastSegment, null);
                    }
                    var fname = TrimOrNull(lastSegment.Substring(0, dlim));
                    var extn = TrimOrNull(lastSegment.Substring(dlim + 1));
                    // if (extn == null || extn == "html" || ValidExtns.Value.Contains(extn))   // ANY match (e.g. "xml" occurs 498 times!)
                    if (extn == null || extn == "html" || ValidExtns.Value.Contains(extn))   // ANY match (e.g. "xml" occurs 498 times!)

                    {
                        return (fname, extn);               // yes. pass extn as-is
                    }
                    return (fname, "html");                 // no. substitute HTML extn (especially from asp/aspx etc)
                }
            }
            return (null, null);
        }

        public static string FilespecLastSegment(string instr)
        {
            string filename, extn;
            (filename, extn) = FileExtLastSegment(instr);
            return (filename == null && extn == null)
                ? null
                : (filename ?? "unknown") + ((extn == null) ? "" : "." + extn);
        }

        /*
        public static string LookupExtnFromMime(string mime)
        {
            if (MimeToExtn.TryGetValue(mime, out var extn))
            {
                return extn;
            }
            Console.WriteLine($"LookupExtnFromMime({mime}) failed");
                "image/x-icon"
                "application/rss+xml"
                "application/json+oembed"
                "application/opensearchdescription+xml", "jpg"
                "button"    http://www.youtube.com/LigonierMinistries
                            http://www.youtube.com/user/LigonierMinistries
                            http://www.youtube.com/user/LigonierMinistries/about?disable_polymer=1
                            http://www.youtube.com/user/LigonierMinistries/channels?disable_polymer=1
                            http://www.youtube.com/user/LigonierMinistries/community?disable_polymer=1
                            http://www.youtube.com/user/LigonierMinistries/playlists?disable_polymer=1
                            http://www.youtube.com/user/LigonierMinistries/videos?disable_polymer=1
                            https://m.youtube.com/user/LigonierMinistries
                            https://m.youtube.com/user/LigonierMinistries/about?disable_polymer=1
                            https://m.youtube.com/user/LigonierMinistries/channels?disable_polymer=1
                            https://m.youtube.com/user/LigonierMinistries/community?disable_polymer=1
                            https://m.youtube.com/user/LigonierMinistries/playlists?disable_polymer=1
                            https://m.youtube.com/user/LigonierMinistries/videos?disable_polymer=1
                "text/xml+oembed", "json"
                "application/x-javascript"
                "application/rss+xml"
            return null;
        }
        */

        /// <summary>
        ///     replace any characters within param by space, but elliding any multiple spaces
        /// </summary>
        /// <param name="rawstr">
        ///     candidate filespec (possibly from webpage title)
        /// </param>
        /// <returns>
        ///     either valid filespec or null
        /// </returns>
        public static string MakeValid(string rawstr)
        {
            const char SPACE = ' ';
            if (string.IsNullOrWhiteSpace(rawstr))
            {
                return null;
            }
            var copy = rawstr.Trim() + CRLF[0];
            var eolIndex = copy.IndexOfAny(CRLF, 2);
            var sb = new StringBuilder(copy.Substring(0, eolIndex).TrimEnd());    // first line only
            for (var i = sb.Length - 1; i >= 0; --i)
            {
                if (!BadChars.Contains(sb[i]))
                {
                    continue;
                }
                // reduce 2 or 3 spaces to 1
                if (i < sb.Length - 1 && sb[i + 1] == SPACE)
                {
                    sb.Remove(i + 1, 1);            // remove following space
                }
                sb[i] = SPACE;                      // substitute char for illegal char
                if (i > 0 && sb[i - 1] == SPACE)
                {
                    sb.Remove(i - 1, 1);            // remove prior space (BTW next iter will re-review the new space)
                }
            }
            var endspec = sb.ToString().Trim();
            //if (rawstr != endspec)
            //{
            //    Console.WriteLine($"MakeValid: {rawstr} -> {endspec}");
            //}
            return (endspec.Length == 0) ? null : endspec;
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
                : raw.Trim();
    }
}
