using System;

namespace DownloadExtractLib.Interfaces
{
    public interface IParsedEvent
    {
        void ParsedProgress(string fromFile, int urlCount, Exception exception = null);
    }
}
