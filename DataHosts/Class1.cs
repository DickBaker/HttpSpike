using System;
using System.Linq;

namespace DataHosts
{
    public class Class1
    {
        readonly WebModel ctx = new WebModel();

        public Class1()
        {
            var topHosts = ctx.Hosts.Where(h => (h.ParentId == null || h.ParentId != 0) && h.HostId > 0).OrderBy(h => h.HostId).Take(10).ToList();
            topHosts.ForEach(h => Console.WriteLine($"{h.HostId}\t{h.HostName}"));
        }
    }
}
