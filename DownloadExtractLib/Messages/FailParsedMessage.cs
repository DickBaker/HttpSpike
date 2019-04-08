using System;

namespace DownloadExtractLib.Messages
{
    public class FailParsedMessage
    {
        public FailParsedMessage(string filespec, Exception exception)
        {
            Filespec = filespec;
            Exception = exception;
        }

        public readonly string Filespec;            // input filespec (probably just downloaded)
        public readonly Exception Exception;        // why parse failed
    }
}
