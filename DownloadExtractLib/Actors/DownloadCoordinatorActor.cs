using Akka.Actor;
using Akka.Event;
using DownloadExtractLib.Interfaces;
using DownloadExtractLib.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using SysDiag = System.Diagnostics;

namespace DownloadExtractLib
{
    public class DownloadCoordinatorActor : ReceiveActor, IDownload
    {
        const int MAXWORKERS = 10,          // maximum child actors (1 host apiece)
            MAXBUSY = 10;                   // maximum downloads queued per host

        static readonly char[] Separators = { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        int ActorNumber;

        string _outPath;

        string OutPath
        {
            get => _outPath;
            set
            {
                _outPath = (string.IsNullOrWhiteSpace(value)) ? null : value.Trim();
                if (_outPath != null)
                {
                    if (_outPath.LastIndexOfAny(Separators) == _outPath.Length - 1)
                    {
                        _outPath = _outPath.Remove(_outPath.Length - 2).TrimEnd();
                    }
                    if (!Directory.Exists(_outPath))
                    {
                        Directory.CreateDirectory(_outPath);
                    }
                }
            }
        }

        readonly ILoggingAdapter _Log = Context.GetLogger();
        IActorRef _ParseCoordinator;

        IActorRef ParseCoordinator => _ParseCoordinator ??
                    (_ParseCoordinator = Context.ActorSelection(ActorNames.ParseCoordinatorActor.Name).Anchor);

        HttpClient Client;

        class WorkDone
        {
            public WorkDone(Uri downloadUri, string filespec)
            {
                DownloadUri = downloadUri;
                Filespec = filespec;
            }

            public readonly Uri DownloadUri;        // FQ with querystring
            public string Url => DownloadUri.ToString();
            public readonly string Filespec;        // absolute filespec (e.g. just downloaded)
        }

        readonly Dictionary<string, int> HostsPending = new Dictionary<string, int>();
        readonly List<DownloadMessage> ToDo = new List<DownloadMessage>();
        readonly Dictionary<string, string> Done = new Dictionary<string, string>();    // key=url, value=filespec
        readonly Dictionary<string, int> DoneByExt = new Dictionary<string, int>();     // key=filetype, value=# completed
        readonly Dictionary<string, Worker> Workers = new Dictionary<string, Worker>();
        readonly IDownloadedEvent CallBack;

        class Worker : IEquatable<Worker>
        {
            public readonly IActorRef ActRef;
            public int MaxActivity = MAXBUSY;                       // TODO: lookup in HOCON
            public List<string> BusyWith = new List<string>();      // active connections
            string WorkerName => ActRef.Path.Name;

            public Worker(IActorRef actRef)
            {
                ActRef = actRef;
            }

            public bool Equals(Worker other) => WorkerName == other.WorkerName;
            public override int GetHashCode() => WorkerName.GetHashCode();
        }

        /// <summary>
        ///     this will be SINGLETON constructor for each STAGENAME ActorSystem
        /// </summary>
        /// <param name="callBack">
        ///     interface so we can invoke IDownloadedEvent.GotItem progress event
        /// </param>
        /// <param name="outPath">
        ///     default output folder (e.g. "C:\temp") for *.html files
        /// </param>
        public DownloadCoordinatorActor(IDownloadedEvent callBack, string outPath)
        {
            CallBack = callBack ?? throw new NullReferenceException("DownloadCoordinatorActor ctor needs non-null IDownloadedEvent callBack");
            var thisfolder = outPath?.Trim();
            if (!string.IsNullOrEmpty(thisfolder))
            {
                if (thisfolder.LastIndexOfAny(Separators) == thisfolder.Length - 1)    // or ArgumentNullException
                {
                    thisfolder = thisfolder.Remove(thisfolder.Length - 2).TrimEnd();
                }
                if (!Directory.Exists(thisfolder))
                {
                    Directory.CreateDirectory(thisfolder);
                }
            }
            OutPath = thisfolder;                               // root output folder [or null if each DownloadMessage.TargetPath is absolute]

            // Create an HttpClientHandler object and set to decompress gzip etc
            var handlr = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            // ToDo: use HttpFactory and Polly !
            Client = new HttpClient(handlr) { Timeout = new TimeSpan(0, 0, 10) };   // 10 sec wait (not 100 sec)

            // Some extra headers since some sites only give proper responses when they are present.
            Client.DefaultRequestHeaders.Add(
                "Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8");
            Client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            Client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            Client.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/64.0.3282.186 Safari/537.36");

            Receive<DownloadMessage>(BeginDownload);
            Receive<DownloadedMessage>(EndDownload);
        }

        /// <summary>
        ///     fresh command to conduct download of network resource
        /// </summary>
        /// <param name="msg">
        ///     message with full Url and File details
        /// </param>
        /// <returns>
        ///     bool to indicate if command has been accepted
        /// </returns>
        /// <remarks>
        ///     the author of this command msg is either .NET caller (Sender=null) or ParseCoordinatorActor (Sender not null)
        /// </remarks>
        public bool BeginDownload(DownloadMessage msg)
        {
            var filespec = msg.TargetPath?.Trim();              // e.g. "C:\temp\abc\def\ghi.html" or "C:\temp\abc\def\"
            SysDiag.Debug.Assert(msg != null, "BeginDownload called with null");
            _Log.Info("BeginDownload: Received DownloadMessage({0})", msg.ToString());

            try
            {
                var url = msg.Url;
                if (Done.TryGetValue(url, out var filespec2))
                {
                    // inform caller work has "completed" (may still be in the queue to be attempted soooon!)
                    Sender.Tell(new DownloadedMessage(msg, filespec2 ?? filespec));
                    return true;                                // processed msg
                }

                if (string.IsNullOrEmpty(filespec))
                {
                    filespec = msg.DownloadUri.Segments[msg.DownloadUri.Segments.Length - 1];   // just get ghi.html as default (from "C:\temp\abc\def\ghi.html")
                }
                if (!Path.IsPathRooted(filespec))
                {
                    var outfs = OutPath ?? throw new InvalidOperationException($"DownloadMessage.TargetPath {filespec} must be absolute as OutPath is null");
                    filespec = Path.Combine(outfs, filespec);
                }
                if (Path.GetExtension(filespec)==".")
                {
                    filespec += ".html";                                    // apply default extension
                }
                Done.Add(msg.Url, filespec);                                // avoid duplicate downloads (NB filename.ext may be changed by DownloadActor)

                var dlmsg2 = (msg.TargetPath == filespec)                   // any changes due to defaults ?
                    ? msg                                                   // no. use original
                    : new DownloadMessage(msg.Url, filespec, msg.EnumDisposition, msg.HtmlDepth);   // yes. create a fresh message as immutable

                var host = dlmsg2.DownloadUri.Host.ToLower();               // e.g. "sales.contoso.com"
                var myWorker = GetWorkerByHost(host);
                if (myWorker == null)                                       // pass to particular worker ?
                {
                    ToDo.Add(dlmsg2);                                       // no. add to queue to do later
                    HostsPending[host] = (HostsPending.ContainsKey(host))   // virgin host ?
                        ? HostsPending[host] + 1                            // no. increment count
                        : 1;                                                // yes. start with 
                }
                else
                {
                    myWorker.BusyWith.Add(dlmsg2.Url);                      // add to worker's work list
                    myWorker.ActRef.Tell(dlmsg2);                           //  and queue to worker's inbox
                }
            }
            catch (Exception exc)
            {
                Sender.Tell(new DownloadedMessage(msg, filespec,HttpStatusCode.ServiceUnavailable, exc));
            }
            return true;                                    // show ActorSystem we handled incoming message
        }

        /// <summary>
        ///     a download we requested has completed.
        ///     1. inject any next message
        ///     2. update internal state
        ///     3. invoke any .NET callback
        /// </summary>
        /// <param name="msg">
        ///     message containing relevant details
        /// </param>
        /// <returns>
        ///     bool to show ActorSystem we digested the message (and are ready for more)
        /// </returns>
        /// <remarks>
        ///     the instigator of this message is either
        ///     1. this [DownloadCoordinatorActor] because the URL has already been got
        ///     2. DownloadActor after genuine GET (either good/bad)
        ///     but in either case, the callback should be invoked (perhaps multiple calls if several parents rely on resource)
        /// </remarks>
        bool EndDownload(DownloadedMessage msg)
        {
            if (msg.Exception == null)
            {
                _Log.Info("BeginDownload: Received DownloadedMessage({0})", msg.ToString());
            }
            else
            {
                _Log.Error("BeginDownload: Failed DownloadedMessage({0}) with {1}", msg, msg.Exception);
            }
            var host = msg.Msg.DownloadUri.Host;
            var thisWorker = Workers[host];
            SysDiag.Debug.Assert(thisWorker.ActRef == Sender, "domain/actor corruption");

            var url = msg.Msg.Url;
            thisWorker.BusyWith.Remove(url);
            Done[url] = msg.FilePath;                   // update the file-extension part
            var extn = msg.FileExt;
            if (extn == ".html")
            {
                ParseCoordinator.Tell(new ParseHtmlMessage(msg.FilePath));
            }
            DoneByExt[extn] = (DoneByExt.ContainsKey(extn)) ? DoneByExt[extn]++ : 1;

            // tell .NET caller current progress
            CallBack?.GotItem(url, null, msg.Exception, 0, 0);
            return true;                                        // show ActorSystem we handled message [expect next one immediately!]
        }

        #region IDownload implementation (for external caller to request work)
        public void FetchHtml(string downloadUrl, string fileName = null)
        {
            var myUri = new Uri(downloadUrl);
            if (!myUri.IsAbsoluteUri)
            {
                throw new InvalidMessageException($"DownloadPage called with invalid Url ({downloadUrl}");
            }
            var msg = new DownloadMessage(myUri, fileName);
            Self.Tell(msg);
        }
        #endregion

        Worker GetWorkerByHost(string host)
        {
            Worker myWorker = null;
            if (Workers.ContainsKey(host))                  // are we already handling domain ?
            {
                myWorker = Workers[host];                   // yes. determine worker [domain on single worker]
                if (myWorker.BusyWith.Count >= myWorker.MaxActivity)     // already at capacity ?
                {
                    myWorker = null;                        // yes. put into queue instead
                }
            }
            else if (Workers.Count < MAXWORKERS)
            {
                var newName = ActorNames.DOWNLOADWORKERROOT + (++ActorNumber);  // e.g. "DownloadActor_1"
                var downloader = Context.ActorOf(Props.Create(() => new DownloadActor(Client)), newName);   // inject parameter to constructor
                myWorker = new Worker(downloader);
                Workers.Add(host, myWorker);
            }

            return myWorker;
        }

        /// <summary>add a supervision strategy to this new parent</summary>
        /// <remarks>the default SupervisorStrategy is a OneForOneStrategy w/ a Restart directive</remarks>
        /// <see> cref="https://github.com/petabridge/akka-bootcamp/blob/master/src/Unit-1/lesson4/README.md"/></see>
        /// <returns>SupervisorStrategy</returns>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            const int MAXNUMBEROFRETRIES = 10,
                MAXSECONDS = 30;
            return new OneForOneStrategy(
                maxNrOfRetries: MAXNUMBEROFRETRIES,
                withinTimeRange: TimeSpan.FromSeconds(MAXSECONDS),
                localOnlyDecider: x =>
                {
                    if (x is ArithmeticException)         // ArithmeticException is application critical ?
                    {
                        return Directive.Resume;          // no. just ignore error and keep failed actor going (drop current message, accept the next)
                                                          //  Resumes message processing using the same actor instance that failed
                    }
                    return (x is NotSupportedException)
                    ? Directive.Stop                  // stop actor permanently
                    : Directive.Restart;              // discards old actor instance and replace with a new one
                });
        }
    }
}