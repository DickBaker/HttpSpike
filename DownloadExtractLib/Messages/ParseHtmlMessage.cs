using System;

namespace DownloadExtractLib.Messages
{
    public class ParseHtmlMessage
    {
        public enum E_mode
        {
            Unused = 0,     // error
            Extract,        // extract HREFs and send to DownloadCoordinator
            Relativise,     // convert absolute URL to relative
            Localise        // convert remote to local resources
        }

        public ParseHtmlMessage(string filespec, string fromUrl = null, bool callDownloader = false)
        {
            Filespec = filespec?.Trim() ?? throw new InvalidOperationException("ParseHtmlMessage: filespec is required");
            Url = string.IsNullOrWhiteSpace(fromUrl) ? null : fromUrl.Trim().ToLower();
            CallDownloader = callDownloader;
        }

        public readonly string Filespec;                    // input filespec (probably just downloaded)
        public string Url;                                  // original Url (necessary if we encounter relative HREFs)
        public readonly bool CallDownloader;

        // public readonly Dictionary<string, string> Remaps;  // key=original URL, value=replacement value

        public override string ToString() => $"Url={Url ?? ""} => file={Filespec}";
    }
}
