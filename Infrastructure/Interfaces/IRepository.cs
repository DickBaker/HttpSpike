using System.Collections.Generic;
using System.Threading.Tasks;
using Infrastructure.Models;

namespace Infrastructure.Interfaces
{
    public interface IRepository
    {
        //Host GetHostByHostname(string hostname);
        //Host GetHostById(int id);
        //Host PutHost(Host host);
        //IEnumerable<Host> GetHosts();
        //IEnumerable<WebPage> GetWebPages();
        //IEnumerable<WebPage> GetWebPagesToDownload();
        //WebPage GetWebPageById(int id);
        //WebPage GetWebPageByUrl(string url);
        //WebPage PutWebPage(WebPage webpage);

        Task<List<ContentTypeToExtn>> GetContentTypeToExtnsAsync();

        Task<List<WebPage>> GetWebPagesToDownloadAsync(int maxrows = 15);

        Task AddLinksAsync(WebPage webpage, IDictionary<string, string> linksDict);

        Task<int> SaveChangesAsync();
    }
}
