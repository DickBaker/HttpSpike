using Akka.Actor;
using Akka.Event;
using DownloadExtractLib.Interfaces;
using DownloadExtractLib.Messages;
using System;
using System.Collections.Generic;
using System.IO;
using SysDiag = System.Diagnostics;

namespace DownloadExtractLib
{
    public class ParseCoordinatorActor : ReceiveActor, IParse
    {
        readonly int MAXWORKERS = Environment.ProcessorCount,   // maximum child worker actors
            MAXBUSY = 2;                                        // maximum messages per worker

        int ActorNumber;
        readonly ILoggingAdapter _Log = Context.GetLogger();

        readonly ActorSelection DownloadCoordinator = Context.ActorSelection(ActorNames.DownloadCoordinatorActor.Path);

        readonly Queue<ParseHtmlMessage> ToDo = new Queue<ParseHtmlMessage>();

        readonly Dictionary<string, Worker> Workers = new Dictionary<string, Worker>();

        readonly IParsedEvent CallBack;

        class Worker : IEquatable<Worker>
        {
            public readonly IActorRef ActRef;
            public int ActiveCount;
            string WorkerName => ActRef.Path.Name;

            public Worker(IActorRef actRef)
            {
                ActRef = actRef;
            }

            public bool Equals(Worker other) => WorkerName == other.WorkerName;
            public override int GetHashCode() => WorkerName.GetHashCode();
        }

        /// <summary>
        ///     delegate to individual ParseActor workers (CPU-bound)
        /// </summary>
        /// <param name="callBack">
        ///     interface so we can invoke IParsedEvent.ParsedProgress progress event
        /// </param>
        /// <remarks>
        /// 1.  e.g. "Customer.html" will be downloaded to "C:\temp\Customer.html"
        /// 2.  but e.g. "style.css" will go to "C:\temp\_files\style.css"
        /// </remarks>
        public ParseCoordinatorActor(IParsedEvent callBack)
        {
            CallBack = callBack ?? throw new NullReferenceException("ParseCoordinatorActor ctor needs non-null IParsedEvent callBack");
            Receive<ParseHtmlMessage>(BeginParse);
            Receive<ParsedHtmlMessage>(EndParsed);
        }

        /// <summary>
        ///     command to parse single file or wildcard
        /// </summary>
        /// <param name="msg">
        ///     full details of single (e.g. C:\a.html) or wildcard (e.g. C:\*.html) filespec
        /// </param>
        /// <returns>
        ///     bool to specify toActorSystem that we accepted this command
        /// </returns>
        /// <remarks>
        ///     command originates from
        ///     1. .NET caller (with callback to get progress notifications)
        ///     2. this ParseCoordinatorActor.ParseFile
        /// </remarks>
        bool BeginParse(ParseHtmlMessage msg)
        {
            var filespec = msg.Filespec;
            _Log.Info($"ParseCoordinatorActor.BeginParse({filespec}) starting");
            if (!File.Exists(filespec))
            {
                Sender.Tell(new ParsedHtmlMessage(filespec, new List<DownloadMessage>(), msg.Url, new FileNotFoundException("BeginParse cannot find file", filespec)));
                return true;            // show ActorSystem that we tried (don't DeadLetter)
            }

            Worker myWorker = null;
            if (Workers.Count < MAXWORKERS)
            {
                var newName = ActorNames.PARSEWORKERROOT + (++ActorNumber);     // e.g. "DownloadActor_1"
                var downloader = Context.ActorOf<ParseActor>(newName);          // parameter-less default constructor
                myWorker = new Worker(downloader);
                Workers.Add(newName, myWorker);
            }
            else
            {
                var busiest = 0;
                foreach (var wkr in Workers)
                {
                    var thisWorker = wkr.Value;
                    if (busiest < thisWorker.ActiveCount)
                    {
                        busiest = thisWorker.ActiveCount;
                        myWorker = thisWorker;
                    }
                }
                if (busiest >= MAXBUSY)
                {
                    ToDo.Enqueue(msg);
                    return true;            // handled (will dequeue later)
                }
            }
            TellParser(msg, myWorker);
            return true;            // handled (passed to child actor)
        }

        /// <summary>
        ///     queue [another] parse request to specific parse actor
        /// </summary>
        /// <param name="msg">
        ///     details of parse command
        /// </param>
        /// <param name="worker">
        ///     specific Worker actor to target command
        /// </param>
        static void TellParser(ParseHtmlMessage msg, Worker worker)
        {
            worker.ActiveCount++;
            worker.ActRef.Tell(msg);
        }

        bool EndParsed(ParsedHtmlMessage msg)
        {
            var sourceName = Sender.Path.Name;
            _Log.Info($"ParseCoordinatorActor.EndParsed from {sourceName}) starting");
            if (!Workers.TryGetValue(sourceName, out var thisWorker))
            {
                SysDiag.Debug.Fail("EndParsed called by unknown source");
                return false;                                       // message not handled (redirect to DeadLetter)
            }

            // alert caller if specified
            CallBack?.ParsedProgress(msg.Filespec, msg.NewDownloads.Count, msg.Exception);  //notify that specific html file fully parsed (maybe finding child refs)

            // TODO: conditionalise with DownloadMessage.Depth (must keep+find orig DownloadMessage by ID)
            foreach (var nextdlmsg in msg.NewDownloads ?? (new List<DownloadMessage>()))
            {
                var myUri = new Uri(nextdlmsg.Url);
                SysDiag.Debug.Assert(myUri.IsAbsoluteUri, "ParseActor must provide absolute Url");
                try
                {
                    DownloadCoordinator.Tell(nextdlmsg);            // DownloadCoordinator (if any else DeadLetterQ) will decide if interesting
                }
                catch (Exception exx)
                {
                    Console.WriteLine(exx);
                }
            }
            // give same ParseActor another file to process, fetching the next in FIFO sequence (no domain/folder priority)
            if (ToDo.Count > 0)                                     // any ParseMessage queued up ?
            {
                var newmsg = ToDo.Dequeue();
                TellParser(newmsg, thisWorker);
            }

            return true;                                        // show ActorSystem we handled message [expect next one immediately!]
        }

        void ParseOne(string fileName, string url)
        {
            var msg = new ParseHtmlMessage(fileName, url);
            Self.Tell(msg);
        }

        /// <summary>add a supervision strategy to this new parent</summary>
        /// <remarks>the default SupervisorStrategy is a OneForOneStrategy w/ a Restart directive</remarks>
        /// <see> cref="https://github.com/petabridge/akka-bootcamp/blob/master/src/Unit-1/lesson4/README.md"/></see>
        /// <returns>SupervisorStrategy</returns>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            const int MAXNUMBEROFRETRIES = 10,
                MAXSECS = 30;
            return new OneForOneStrategy(
                maxNrOfRetries: MAXNUMBEROFRETRIES,
                withinTimeRange: TimeSpan.FromSeconds(MAXSECS),
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

        #region IParse implementation (for external caller to request work)
        public void ParseFile(string fileName, string url = null)
        {
            if (File.Exists(fileName))
            {
                ParseOne(fileName, url);
            }
        }

        public void LocaliseFile(string fileName, Dictionary<string, string> remaps, string url = null) =>
            throw new NotImplementedException();
        #endregion
    }
}
