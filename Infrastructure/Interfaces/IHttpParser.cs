using System.Collections.Generic;

namespace Infrastructure.Interfaces
{
    public interface IHttpParser
    {
    //  Task LoadFromWebAsync(string url, CancellationToken cancellationToken);
        void LoadFromFile(string url, string path);
        void ReworkLinks(string url, string filespec, Dictionary<string, string> oldNewLinks);
        IDictionary<string, string> GetLinks();
        void SaveFile(string filespec);
        string Title { get; set; }
    }
}
