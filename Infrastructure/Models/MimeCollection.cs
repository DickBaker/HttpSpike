using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Infrastructure.Models
{
    public static class MimeCollection
    {
        // TODO: or use SortedList<TKey,TValue>() ??
        //public static IDictionary<string, string> MimeToExtn { get; } = new Dictionary<string, string>();
        // MimeToExtn has 637 entries, whereas distinct Extns number only 78 so more efficient search
        // cf. SortedList or Hash or HashSet

        const int maxlen = 650;
        static HashSet<string> ValidExtns;
#if DEBUG
#endif
        static readonly IDictionary<string, ContentTypeToExtn> MimeDict = new SortedDictionary<string, ContentTypeToExtn>();

        public static List<string> MissingExtns { get; } = new List<string>();

        public static bool IsValidExtn(string extn)
        {
            if (ValidExtns.Contains(extn))
            {
                return true;
            }
#if DEBUG
            if (!MissingExtns.Contains(extn))
            {
                MissingExtns.Add(extn);       // track for end of prog
            }
#endif
            return false;
        }

        public static void Load(IEnumerable<ContentTypeToExtn> mimeEnum)
        {
            IDictionary<string, ContentTypeToExtn> MimeList = new SortedList<string, ContentTypeToExtn>(maxlen);    // rival type for perf consideration
            MimeDict.Clear();
            MimeList.Clear();
            foreach (var item in mimeEnum)
            {
                MimeDict.Add(item.Template, item);
                MimeList.Add(item.Template, item);
            }
            Debug.Assert(MimeDict.Except(MimeList).ToList().Count == 0, "different ValidExtns !!");     // uses IEqualityComparer<ContentTypeToExtn>

            AddCtteIfMissing(MimeDict, "text/xml+oembed", "json");              // these were found in real-world but not in official docs
            AddCtteIfMissing(MimeDict, "application/x-javascript", "js");
            AddCtteIfMissing(MimeDict, "application/rss+xml", "xml");

            ValidExtns = new HashSet<string>(MimeDict.Values.Distinct().Select(ctte => ctte.Extn));
            var ValidExtns2 = new HashSet<string>(MimeList.Values.Distinct().Select(ctte => ctte.Extn));
            Debug.Assert(ValidExtns.SetEquals(ValidExtns2), "different ValidExtns !!");
            // commonplace extns found in real-world but [maybe?] not in ContentTypeToExtns
            AddIfMissing(ValidExtns, new string[] { "asp", "aspx", "exe", "htm", "jpeg", "mp3", "php" });
        }

        static void AddIfMissing(ICollection<string> collection, string[] dontforget)
        {
            foreach (var extn in dontforget)
            {
                if (!collection.Contains(extn))
                {
                    collection.Add(extn);
                }
            }
        }

        static void AddCtteIfMissing(IDictionary<string, ContentTypeToExtn> collection, string template, string extn, bool isText = true)
        {
            if (!collection.ContainsKey(template))
            {
                collection.Add(template, new ContentTypeToExtn(template, extn, isText));
            }
        }

        public static ContentTypeToExtn FindExtn(string template) => MimeDict.TryGetValue(template, out var val) ? val : null;

        public static string LookupExtnFromMime(string mime) => MimeDict.TryGetValue(mime, out var ext1) ? ext1.Extn : null;
    }
}
