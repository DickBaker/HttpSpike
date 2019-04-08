using System;
using System.Collections.Generic;

namespace DownloadExtractLib.Messages
{
    public class ParsedHtmlMessage
    {
        public ParsedHtmlMessage(string filespec, List<DownloadMessage> dlmsgs, string fromUrl = null, Exception exception = null)
        {
            Filespec = filespec;
            NewDownloads = dlmsgs ?? new List<DownloadMessage>();
            Url = fromUrl;
            Exception = exception;
        }

        public readonly string Filespec;        // absolute filespec (e.g. just downloaded)
        public string Url;                      // original Url (necessary if we encounter relative HREFs)
        public readonly List<DownloadMessage> NewDownloads;   // distinct HREFs discovered
        public readonly Exception Exception;    // any problems during the process

        public override string ToString() => $"Url={Url ?? string.Empty}\t-[{NewDownloads.Count}]>\tfile={Filespec}";
    }
}
