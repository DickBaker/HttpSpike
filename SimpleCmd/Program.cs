using System;
using System.Threading.Tasks;
using Akka.Actor;
using DownloadExtractLib;
using DownloadExtractLib.Interfaces;
using DownloadExtractLib.Messages;

namespace SimpleCmd
{
    class Program : IDownloadedEvent, IParsedEvent
    {
        const string ACTSYSNAME = "Theatre";                         // origin unknown !
        TaskCompletionSource<bool> _tcs;

        static void Main()
        {
            var sut = new Program();
            //sut.ParseTest();                    // just test parse
            sut.DownloadTest();                 // just test download

            Console.ReadLine();                 // don't exit prematurely
        }

        public void ParseTest()
        {
            //const string PARSEFILE = @"C:\dev\HttpSpike\SimpleCmd\Samples\Entity Framework Core 2.1_ What's New Playbook.html",
            //    FROMURL = "https://app.pluralsight.com/player";         // stripped of querystring
            const string PARSEFILE = @"C:\dev\HttpSpike\SimpleCmd\Samples\Entity Framework Core 2.1_ What's New Playbook.html",
                FROMURL = "https://www.packtpub.com/packt/offers/free-learning";         // stripped of querystring

            var theatre = ActorSystem.Create(ACTSYSNAME);
            _tcs = new TaskCompletionSource<bool>();

            //Props props = Props.Create(() => new DownloadCoordinatorActor(this, OUTPATH)).WithRouter(FromConfig.Instance);
            var pca = theatre.ActorOf(Props.Create(() => new ParseCoordinatorActor(this)), ActorNames.ParseCoordinatorActor.Name);

            // command Parser to start with single file.
            // The ParseCoordinatorActor will attempt to command the Download process, but this will go to DeadLetter Q instead
            pca.Tell(new ParseHtmlMessage(filespec: PARSEFILE, fromUrl: FROMURL));      // no child downloads

            _tcs.Task.Wait();                   // ensure all parsing has completed (CPU-bound)

            // shut down the DownloadCoordinatorActor
            pca.Tell(PoisonPill.Instance);

            // shut down the ActorSystem
            theatre.Terminate().Wait();         // Theatre.WhenTerminated.Wait();
        }

        void DownloadTest()
        {
            const string
                DOWNLOADURL = "https://www.packtpub.com/packt/offers/free-learning",
                OUTPATH = @"C:\temp\stage",
                OUTFILE = "free-learning.html";
            var theatre = ActorSystem.Create(ACTSYSNAME);
            _tcs = new TaskCompletionSource<bool>();

            //Props props = Props.Create(() => new DownloadCoordinatorActor(this, OUTPATH)).WithRouter(FromConfig.Instance);
            var dlca = theatre.ActorOf(Props.Create(() => new DownloadCoordinatorActor(this, OUTPATH)), ActorNames.DownloadCoordinatorActor.Name);
            dlca.Tell(new DownloadMessage(downloadUrl: DOWNLOADURL, targetPath: OUTFILE));      // no parsing or child downloads

            _tcs.Task.Wait();                   // ensure all I/O has completed

            // shut down the DownloadCoordinatorActor
            dlca.Tell(PoisonPill.Instance);     // beware PipeTo still in the mix

            // shut down the ActorSystem
            theatre.Terminate().Wait();         // Theatre.WhenTerminated.Wait();
        }

        #region IDownloadedEvent
        public void GotItem(string parentUrl, string childUrl, Exception exception, int totalRefs, int doneRefs)
        {
            if (exception == null)
            {
                Console.WriteLine($"\tGotItem:\tparent={parentUrl},\tchild={childUrl},\ttotalRefs={totalRefs},\tdoneRefs={doneRefs}");
                _tcs.SetResult(result: true);                // TODO: return false if any 404, or SetException if we hit any
            }
            else
            {
                Console.WriteLine($"\tGotItem:\tparent={parentUrl},\tchild={childUrl},\tresult={exception},\ttotalRefs={totalRefs},\tdoneRefs={doneRefs}");
                _tcs.SetException(exception);                // TODO: return false if any 404, or SetException if we hit any
            }
        }
        #endregion

        #region IParsedEvent
        public void ParsedProgress(string fromFile, int urlCount, Exception exception = null)
        {
            if (exception == null)
            {
                Console.WriteLine($"ParsedProgress:\tfromFile={fromFile},\ttotalRefs={urlCount}");
                _tcs.SetResult(result: true);                // TODO: return false if any 404, or SetException if we hit any
            }
            else
            {
                Console.WriteLine($"ParsedProgress:\tfromFile={fromFile},\ttotalRefs={urlCount},\tresult={exception}");
                _tcs.SetException(exception);                // TODO: return false if any 404, or SetException if we hit any
            }
        }
        #endregion
    }
}