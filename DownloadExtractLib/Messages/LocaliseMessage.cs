using System.Collections.Generic;

namespace DownloadExtractLib.Messages
{
    public class LocaliseMessage
    {
        public LocaliseMessage(string filespec, Dictionary<string, string> remaps, string fromUrl = null)
        {
            Filespec = filespec;
            Remaps = remaps;
            Url = fromUrl;
        }

        public readonly string Filespec;                    // input filespec (probably just downloaded)
        public string Url;                                  // original Url (necessary if we encounter relative HREFs)
        public readonly Dictionary<string, string> Remaps;  // key=original URL, value=replacement value
    }
}
