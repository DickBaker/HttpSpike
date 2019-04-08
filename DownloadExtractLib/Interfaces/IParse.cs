using System.Collections.Generic;

namespace DownloadExtractLib.Interfaces
{
    public interface IParse
    {
        void ParseFile(string fileName, string url = null);
        void LocaliseFile(string fileName, Dictionary<string, string> remaps, string url = null);
    }
}
