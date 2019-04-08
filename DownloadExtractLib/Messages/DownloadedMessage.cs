using System;
using System.Net;

namespace DownloadExtractLib.Messages
{
    public class DownloadedMessage
    {
        public DownloadedMessage(DownloadMessage msg, string filePath, HttpStatusCode statusCode = HttpStatusCode.BadRequest, Exception exception = null)
        {
            Msg = msg;                              // original DownloadMessage command
            FilePath = filePath;                    // the DownloadActor may have changed filename and/or extension from initial DownloadMessage.FilePath
            StatusCode = statusCode;
            Exception = exception;
        }

        public readonly DownloadMessage Msg;
        public readonly string FilePath;            // always with file-extension. NB may differ from original Msg.TargetPath
        public readonly HttpStatusCode StatusCode;
        public readonly Exception Exception;        // null, or why download failed

        public string FileExt                       // if called >once, better if on FilePath Set
        {
            get                                     //  just the file-extension
            {
                if (string.IsNullOrWhiteSpace(FilePath))
                {
                    return null;
                }
                var fi = new System.IO.FileInfo(FilePath.Trim().ToLower());
                var extn = fi.Extension;
                return extn == ".htm" ? ".html" : extn;
            }
        }
    }
}
