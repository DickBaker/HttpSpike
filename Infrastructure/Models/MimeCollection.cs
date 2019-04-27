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
        static readonly IDictionary<string, ContentTypeToExtn> MimeSorted = new SortedList<string, ContentTypeToExtn>(maxlen);

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
            MimeDict.Clear();
            MimeSorted.Clear();
            foreach (var item in mimeEnum)
            {
                MimeDict.Add(item.Template, item);
                MimeSorted.Add(item.Template, item);
            }
            Debug.Assert(MimeDict.Except(MimeSorted).ToList().Count == 0, "different ValidExtns !!");     // uses IEqualityComparer<ContentTypeToExtn>
            ValidExtns = new HashSet<string>(MimeDict.Values.Distinct().Select(ctte => ctte.Extn));
            var ValidExtns2 = new HashSet<string>(MimeSorted.Values.Distinct().Select(ctte => ctte.Extn));
            Debug.Assert(ValidExtns.SetEquals(ValidExtns2), "different ValidExtns !!");
            ValidExtns.Add("asp");              // these commonplace extns found in real-world but not listed in ContentTypeToExtns
            ValidExtns.Add("aspx");
            ValidExtns.Add("htm");
            ValidExtns.Add("jpeg");
            ValidExtns.Add("mp3");
            ValidExtns.Add("php");
        }

        public static ContentTypeToExtn FindExtn(string template) => MimeDict.TryGetValue(template, out var val) ? val : null;

        public static string LookupExtnFromMime(string mime) => MimeDict.TryGetValue(mime, out var ext1) ? ext1.Extn : null;
        /*
            "text/xml+oembed", "json"
            "application/x-javascript"
            "application/rss+xml"
        */

    }
}
