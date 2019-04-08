using DownloadExtractLib.Messages;

namespace DownloadExtractLib.Interfaces
{
    public interface IDownloaded
    {
        void EndDownload(DownloadedMessage msg);
    }
}
