using CommandLine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace HttpSpike
{
    internal class Program
    {
        static string MyUrl;
        protected static List<string> DoneUrls = new List<string>();

        public static void Main(string[] args)
        {
            var myProg = new Program();
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(opts => myProg.RunOptionsAndReturnExitCode(opts))
                .WithNotParsed(myProg.HandleParseError);

            MyUrl = args[0];
            var rslt = myProg.GetWeb(MyUrl);
            var rslt2 = rslt.Result;
        }

        void HandleParseError(IEnumerable<Error> errs)
            => throw new NotImplementedException();

        void RunOptionsAndReturnExitCode(Options opts)
        {
            var tFiles = opts.InputFiles.Select(DoFile);
            var tPages = opts.InputUrls.Select(GetWeb);
            var both = tPages.Union(tFiles).ToArray();

            Task.WaitAll(both);
        }

        public async Task<List<string>> GetWeb(string theUrl)
        {
            var morePages = new List<string>();
            theUrl = theUrl.ToLower();
            if (!DoneUrls.Contains(theUrl))
            {
                DoneUrls.Add(theUrl);
                string pageContent;
                using (var client = new HttpClient())
                {
                    pageContent = await client.GetStringAsync(theUrl).ConfigureAwait(continueOnCapturedContext: false);
                }

                var hap = new HtmlAgilityPack.HtmlWeb();
                var hdoc = await hap.LoadFromWebAsync(theUrl).ConfigureAwait(continueOnCapturedContext: false);
                //var hrefs = hdoc.s
            }
            return morePages;
        }

        public async Task DoFile(string path)
        {
            var morePages = new List<string>();
            path = path?.Trim();
            string fileContent;
            using (var sr = File.OpenText(path))
            {
                fileContent = await sr.ReadToEndAsync().ConfigureAwait(continueOnCapturedContext: false);
            }

            if (path.EndsWith(".mhtm", StringComparison.InvariantCultureIgnoreCase))
            {
                ProcessSimple(path, fileContent);
            }
            else
            {
                ProcessMhtml(path, fileContent);
            }
        }

        static void ProcessMhtml(string path, string rslt) => throw new NotImplementedException();
        static void ProcessSimple(string path, string rslt) => throw new NotImplementedException();

        class Options
        {
            [Option('f', "file", Required = false, HelpText = "Input file(s) to be processed")]
            public IEnumerable<string> InputFiles { get; set; }

            [Option('u', "url", Required = false, HelpText = "Input urls(s) to be processed")]
            public IEnumerable<string> InputUrls { get; set; }

            [Option('o', "out", Required = false, HelpText = "Output folder")]
            public IEnumerable<string> OutPath { get; set; }

            [Verb("link", HelpText = "local or remote")]
            public class LinkOptions
            {
                [Option('l', "link", Required = false, HelpText = "hyperlink replace")]
                public IEnumerable<string> Link { get; set; }
            }
        }
    }
}
