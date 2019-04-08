using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace DownloadExtractLib.Messages
{
    public class DownloadMessage : IEquatable<DownloadMessage>
    {
        public enum E_FileDisposition
        {
            LeaveIfExists,      // check: if exists abort download replacement and suppose existing is same
            Revector,           // check: if exists write to new file as replacement
            RevectorAndCompare, // check: if exists write to new file, compare and drop new if old=new
            OverwriteAlways     // don't check, so any old file will get overwritten [tough!]
        }

        static int RequestID = 0;

        public DownloadMessage(string downloadUrl, string targetPath, E_FileDisposition enumDisposition = E_FileDisposition.OverwriteAlways, int htmlDepth = 0)
            : this(new Uri(downloadUrl.ToLower()), targetPath, enumDisposition, htmlDepth)
        { }

        public DownloadMessage(Uri uri, string targetPath, E_FileDisposition enumDisposition = E_FileDisposition.OverwriteAlways, int htmlDepth = 0)
        {
            ID = unchecked(Interlocked.Increment(ref RequestID));
            DownloadUri = uri ?? throw new NullReferenceException("DownloadMessage requires non-null Uri"); // assume caller has lowercased strings
            Url = DownloadUri.ToString();               // set alternate format as 1-off
            var segs = uri.Segments;                    // split the url (except querystring)
            TargetPath = (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(targetPath)))
                ? Path.Combine(targetPath, segs[segs.Length - 1] + ".html") // default filename to final segment of Url
                : targetPath;
            EnumDisposition = enumDisposition;
            HtmlDepth = htmlDepth;
        }

        public readonly int ID;
        public readonly Uri DownloadUri;                // FQ with querystring
        public string Url;
        public readonly string TargetPath;              // FQ filespec (possibly no file-extension)
        public readonly E_FileDisposition EnumDisposition;
        public readonly int HtmlDepth;

        public override string ToString() => $"{ID}: Url={Url} -> file={TargetPath}";

        /// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns><see langword="true" /> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <see langword="false" />.</returns>
        public bool Equals(DownloadMessage other)
        {
            if (other == null || !Url.Equals(other.Url, StringComparison.InvariantCultureIgnoreCase))
            {
                return false;
            }
#if DEBUG
            if (!TargetPath.Equals(other.TargetPath, StringComparison.InvariantCultureIgnoreCase))
            {
                Debug.Fail($"same download Url ({Url}) but different file targets ({TargetPath}, {other.TargetPath})");
            }

            return true;
#else
            return Url.Equals(other.Url, StringComparison.InvariantCultureIgnoreCase);
#endif
        }

        public override int GetHashCode() => Url.GetHashCode();
    }
}
