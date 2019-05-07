using System;
using System.Collections.Generic;

namespace Infrastructure.Interfaces
{
    public interface IHttpParser
    {
        Uri BaseAddress { get; set; }                       // property that caller must be able to R+W

        //  Task LoadFromWebAsync(string url, CancellationToken cancellationToken);
        void LoadFromFile(string url, string path);
        bool ReworkLinks(string filespec, IDictionary<string, string> oldNewLinks);
        IDictionary<string, string> GetLinks();
        void SaveFile(string filespec);
        string Title { get; set; }
    }
}
