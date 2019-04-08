using System;

namespace DownloadExtractLib.Interfaces
{
    public interface IDownloadedEvent
    {
        void GotItem(string parentUrl, string childUrl, Exception exception, int totalRefs, int doneRefs);
    }
}
