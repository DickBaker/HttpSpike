using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Infrastructure.Models
{
    public static class MimeCollection
    {
        const int maxlen = 650;
        static HashSet<string> ValidExtns;
        static readonly IDictionary<string, ContentTypeToExtn> MimeDict = new SortedDictionary<string, ContentTypeToExtn>();
        static readonly IDictionary<string, ContentTypeToExtn> MimeSorted = new SortedList<string, ContentTypeToExtn>(maxlen);

        public static void Load(IEnumerable<ContentTypeToExtn> mimeEnum)
        {
            foreach (var item in mimeEnum)
            {
                MimeDict.Add(item.Template, item);
                MimeSorted.Add(item.Template, item);
            }
            Debug.Assert(MimeDict.Except(MimeSorted).ToList().Count == 0, "different ValidExtns !!");     // uses IEqualityComparer<ContentTypeToExtn>
            ValidExtns = new HashSet<string>(MimeDict.Values.Distinct().Select(ctte => ctte.Extn));
            var ValidExtns2 = new HashSet<string>(MimeSorted.Values.Distinct().Select(ctte => ctte.Extn));
            Debug.Assert(ValidExtns.Except(ValidExtns2).ToList().Count == 0, "different ValidExtns !!");
        }

        public static ContentTypeToExtn FindExtn(string template)
        {
            return (MimeDict.TryGetValue(template, out var val)) ? val : null;
        }

        public static string LookupExtnFromMime(string mime)
        {
            var rslt1 = MimeDict.TryGetValue(mime, out var ext1);
            var rslt2 = MimeSorted.TryGetValue(mime, out var ext2);
            if (rslt1 != rslt2 || ext1 != ext2)
            {
                Console.WriteLine("LookupExtnFromMime failed");
                return null;
            }
            return rslt1 ? ext1.Extn : null;
        }
    }
}
