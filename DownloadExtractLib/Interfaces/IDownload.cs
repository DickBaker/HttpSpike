namespace DownloadExtractLib.Interfaces
{
    public interface IDownload
    {
        void FetchHtml(string downloadUrl, string fileName = null);
    }
}
