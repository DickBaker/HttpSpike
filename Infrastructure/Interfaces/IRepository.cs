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
        Task<WebPage> GetWebPageByUrlAsync(string url);
        //WebPage PutWebPage(WebPage webpage);

        Task AddLinksAsync(WebPage webpage, IDictionary<string, string> linksDict);

        WebPage AddWebPage(WebPage newpage);
        Task<List<ContentTypeToExtn>> GetContentTypeToExtnsAsync();

        Task<List<WebPage>> GetWebPagesToDownloadAsync(int maxrows = 15);

        Task<List<WebPage>> GetWebPagesToLocaliseAsync(int maxrows = 15);

        Task<int> SaveChangesAsync();
        int SaveChanges();
    }
}
