using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using DownloadExtractLib;
using DownloadExtractLib.Interfaces;
using DownloadExtractLib.Messages;

namespace DownloadExtractLibTest
{
    public class DownloadCoordinatorActorUT : TestKit, IDownloadedEvent
    {
        const string DOWNLOADURL = "", OUTPATH = @"C:\temp\stage";
        readonly TaskCompletionSource<bool> _tcs = new TaskCompletionSource<bool>();

        [Fact]
        public void TestMethod1()
        {
            // Arrange (TestKit has already established the Actor System)
            var dlca = Sys.ActorOf(Props.Create(() => new DownloadCoordinatorActor(this, OUTPATH)));
            _tcs.Task.Wait();                // ensure all I/O has completed
            // Act
            dlca.Tell(new DownloadMessage(DOWNLOADURL, "ch029-ch03s03.htm"));

            // Assert
            var fileExists = File.Exists(OUTPATH + @"\ch029-ch03s03.html");
            Assert.True(fileExists);
        }

         void IDownloadedEvent.GotItem(string parentUrl, string childUrl, int result, int totalRefs, int doneRefs)
        {
            System.Console.WriteLine($"\tTellMe:\tparent={parentUrl},\tchild={childUrl},\tresult={result},\ttotalRefs={totalRefs},\tdoneRefs={doneRefs}");
            if (totalRefs == doneRefs)
            {
                System.Console.WriteLine($"TellMe:\tparent={parentUrl}\tCOMPLETED");
                _tcs.SetResult(result: true);                // TODO: return false if any 404, or SetException if we hit any
            }
        }
    }
}
