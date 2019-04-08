using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;

namespace DownloadExtractLib
{
    public class Stage : IDisposable
    {
        ActorSystem allTheWorld;
        ActorSystem AllTheWorld
        {
            get
            {
                return allTheWorld ?? (allTheWorld = ActorSystem.Create(ActorNames.STAGENAME));
            }
        }

        readonly string OutPath;
        IActorRef DownloadCoordinator;
        IActorRef ParseCoordinator;
        Dictionary<string, TaskCompletionSource<string>> InProgressQ = new Dictionary<string, TaskCompletionSource<string>>();

        public Stage(string outPath)
        {
            OutPath = outPath ?? throw new InvalidOperationException("Must specify target directory on Stage creation");
            if (!System.IO.Directory.Exists(OutPath))
            {
                Directory.CreateDirectory(OutPath);             // create folder or die
            }

            // Drama = AllTheWorld.ActorOf(Props.Create(() => new DramaActor(DownloadCoordinator, OutPath)),"DramaActor");
            ParseCoordinator = AllTheWorld.Value.ActorOf(
                Props.Create(() => new ParseCoordinatorActor()), ActorNames.ParseCoordinatorActor.Name);
            DownloadCoordinator = AllTheWorld.Value.ActorOf(
                Props.Create(() => new DownloadCoordinatorActor(OutPath)), ActorNames.DownloadCoordinatorActor.Name);
        }

        public void ParseFile(string fileName) =>
            ParseCoordinator.Tell(new Messages.ParseMessage(fileName));

        public Task<string> DownloadPage(string downloadUrl)
        {
            var myUri = new Uri(downloadUrl.Trim().ToLower());
            if (myUri.IsAbsoluteUri)
            {
                throw new InvalidOperationException($"DownloadPage({downloadUrl} is invalid");
            }
            downloadUrl = myUri.ToString();                     // standardise syntax
            if (InProgressQ.ContainsKey(downloadUrl))
            {
                return Task.FromResult(string.Empty);           // show already queued
            }
            var tcs = new TaskCompletionSource<string>();
            InProgressQ[downloadUrl] = tcs;
            DownloadCoordinator.Tell(new Messages.DownloadMessage(downloadUrl, OutPath));
            return tcs.Task;
        }

        public Task Terminate() => AllTheWorld.Terminate();

        public void Dispose()
        {
            const long WAITMS = 1000;
            foreach (var item in InProgressQ)
            {
                var tcs = item.Value;
                tcs.SetCanceled();
            }
            InProgressQ.Clear();
            var closeList = new List<Task<bool>>();
            if (ParseCoordinator != null)
            {
                closeList.Add(ParseCoordinator.GracefulStop(TimeSpan.FromMilliseconds(WAITMS)));
                ParseCoordinator = null;
            }
            if (DownloadCoordinator != null)
            {
                closeList.Add(DownloadCoordinator.GracefulStop(TimeSpan.FromMilliseconds(WAITMS)));
                DownloadCoordinator = null;
            }
            if (closeList.Count > 0)
            {
                Task.WaitAll(closeList.ToArray());
            }
            AllTheWorld.Terminate().Wait();
            allTheWorld = null;                         // force any new usage to start over
        }
    }
}
