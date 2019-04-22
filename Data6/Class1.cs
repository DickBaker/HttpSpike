using System.Linq;

namespace Data6
{
    public class Class1
    {
        public Class1()
        {
            var ctx = new Model6();
            var templates = ctx.ContentTypeToExtns.ToList();
            System.Console.WriteLine(templates.Count);
            var wps = ctx.WebPages.Where(w => w.PageId == 23700).FirstOrDefault();
            System.Console.WriteLine(wps.PageId);
        }
    }
}
