namespace DownloadExtractLib
{
    using Akka.Actor;
    using Akka.Event;
    using DownloadExtractLib.Messages;
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using SysDiag = System.Diagnostics;

    /// <summary>
    /// Defines the <see cref="DownloadActor" />
    /// </summary>
    public class DownloadActor : ReceiveActor
    {
        #region Fields

        /// <summary>
        /// Defines the _Log
        /// </summary>
        internal readonly ILoggingAdapter _Log = Context.GetLogger();

        /// <summary>
        /// Defines the Client
        /// </summary>
        internal readonly HttpClient Client;

        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="DownloadActor"/> class.
        /// </summary>
        public DownloadActor(HttpClient client)
        {
            Client = client;
            Receive<DownloadMessage>(DownloadPage);
            Receive<GotContentMessage>(GotDownloadPage);
        }
        #endregion

        #region Methods
        /*
        bool DownloadPage(DownloadMessage msg)
        {
            var origmsg = msg;
            var tcs = new TaskCompletionSource<GotContentMessage>();
            var gotMsg = new GotContentMessage(msg);
            Client.GetAsync(msg.DownloadUri,HttpCompletionOption.ResponseContentRead)
                .ContinueWith((response, origmsg2) => { response.Result.Content.ReadAsStringAsync();
            return tcs.Task;
                })
                .ContinueWith(t2=> DoResponse(t2))
                .PipeTo(Self);
                return true;                                        // show ActorSystem we handled message [expect next one immediately!]
        }
        */
        /// <summary>
        ///     process incoming DownloadMessage
        /// </summary>
        /// <param name="msg">incoming msg<see cref="DownloadMessage"/></param>
        /// <returns>true to state that message has been accepted</returns>
        internal bool DownloadPage(DownloadMessage msg)
        {
            var uri = msg.DownloadUri;
            SysDiag.Debug.Assert(uri.IsAbsoluteUri, $"DownloadActor.DownloadPage({uri}) called with non-absolute Url");
            var fs = msg.TargetPath;
            var fi = new FileInfo(fs);
            var dn = fi.DirectoryName;              // string representing the directory's full path
            if (Directory.Exists(dn))
            {
                if (msg.EnumDisposition == DownloadMessage.E_FileDisposition.LeaveIfExists
                    && File.Exists(fs))
                {
                    var failmsg = new DownloadedMessage(msg, fs, HttpStatusCode.NotModified);
                    Sender.Tell(failmsg);
                }
            }
            else
            {
                _Log.Info("DownloadPage creating directory {0} for {1}.{2}", dn, fi.Name, fi.Extension);
                Directory.CreateDirectory(dn);
            }

            // preserve volatile state now (before t1 that starts hot) for use by subsequent completion
            var ctx1 = new Context1(Sender, msg);
            var t1 = Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);    // returns Task<HttpResponseMessage>
            var t2A = t1.ContinueWith((req, state) =>
            {
                var ctx1A = state as Context1;                              // recover entry context
                var dlmsg = ctx1A.DownloadMessage;
                var status = req.Result.StatusCode;
                if (req.IsFaulted)
                {
                    var s = "";
                    Console.WriteLine();
                    PostBack(dlmsg, ctx1A, status, req.Exception);
                }
                else
                {
                    var hrm = req.Result;
                    try
                    {
                        hrm.EnsureSuccessStatusCode();                      // throw if !IsSuccessStatusCode

                        // TODO: refine the destination filename.extn from the response

                        // TODO: decide if file already exists and if overwrite/transform

#pragma warning disable GCop302 // Since '{0}' implements IDisposable, wrap it in a using() statement
                        var outfs = new FileStream(fs, FileMode.Create);                // open stream to write file. Disposed later !
#pragma warning restore GCop302 // Since '{0}' implements IDisposable, wrap it in a using() statement
                        _Log.Info($"DownloadActor.DownloadPage({msg.Url} => {fs}) started");
                    }
                    catch (Exception excp1)
                    {
                        PostBack(dlmsg, ctx1A, status, excp1);
                    }
                }
            }, ctx1);
            var t2 = t1.ContinueWith(GetStuff,
                    ctx1,
                    TaskContinuationOptions.OnlyOnRanToCompletion);         // happy path (no cancel/fault)

            var t2F = t1.ContinueWith((req2, state) =>
                {
                    var ctx1f = (Context1)state;
                },
                TaskContinuationOptions.NotOnRanToCompletion);
            var t3 = t2.ContinueWith(t_gotContent)
                .PipeTo(Self);                                              // Task
            return true;                                        // show ActorSystem we handled message [expect next one immediately!]
        }

        /// <summary>
        /// process response header, decoding for text or binary and write to file [in+out streams by chunks]
        /// </summary>
        /// <param name="reqtask">antecedent</param>
        /// <param name="state">Context1</param>
        /// <returns>Task<GotContentMessage></returns>
        internal Task<GotContentMessage> GetStuff(Task<HttpResponseMessage> reqtask, object state)
        {
            var ctx1 = state as Context1;
            _Log.Info($"DownloadActor.GetStuff({ctx1.DownloadMessage.Url}) response header {reqtask.Status}");
            var hrm = reqtask.Result;                   // non-blocking as antecedent Task<HttpResponseMessage> had completed
            hrm.EnsureSuccessStatusCode();              // throw if !IsSuccessStatusCode

            var fs = ctx1.DownloadMessage.TargetPath;
            var fi = new FileInfo(fs);
            var di = fi.Directory;
            if (!di.Exists)
            {
                di.Create();                            // short-term block as no async method available
            }
            var filestrmOut = File.Create(fs);          // ?? need an overload that sets FileOptions.Asynchronous ??
            var copydone = hrm.Content.CopyToAsync(filestrmOut);
            copydone.ContinueWith<GotContentMessage>((antecedent) => FinishOff2(antecedent, ctx1), TaskContinuationOptions.OnlyOnRanToCompletion);

            var tcs = new TaskCompletionSource<GotContentMessage>();        // puppet that will return a GotContentMessage result
            /*
            // Create a file using the FileStream class
            int BUFFLEN = 4096;
            var fsWrite = File.Create(fs, BUFFLEN, FileOptions.Asynchronous);

            //*** DEBUG ***
            var TEMPsrc = File.OpenText(@"C:\dev\HttpSpike\GCop.json");
            var TEMPxfr = TEMPsrc.BaseStream.CopyToAsync(fsWrite, BUFFLEN);
            await TEMPxfr;
            fsWrite.Close();
            TEMPsrc.Close();
            */

            if (hrm.Content.Headers.ContentType == null)
            {
                // stream-in the binary content
            }
            var taskTxt = hrm.Content.ReadAsStringAsync();

            /*
            var contnt = await reqtask.Result.Content.ReadAsStringAsync().ConfigureAwait(continueOnCapturedContext: false);
            try
            {
                if (state != null && reqtask.Result.IsSuccessStatusCode)
                {
                    tcs.SetResult(new GotContentMessage(rcm, contnt));
                }
                else
                {
                    tcs.SetException(new ApplicationException($"download failed ({reqtask.Result.StatusCode})"));
                }
            }
            catch (Exception excp1)
            {
                tcs.SetException(excp1);
            }
            */
            return tcs.Task;                          // unwrap the GotContentMessage
        }

        /// <summary>
        /// The PostBack
        /// </summary>
        /// <param name="dlmsg">The dlmsg<see cref="DownloadMessage"/></param>
        /// <param name="ctx1A">The ctx1A<see cref="Context1"/></param>
        /// <param name="status">The status<see cref="HttpStatusCode"/></param>
        /// <param name="excp1">The excp1<see cref="Exception"/></param>
        private static void PostBack(DownloadMessage dlmsg, Context1 ctx1A, HttpStatusCode status, Exception excp1)
        {
            var failmsg = new DownloadedMessage(dlmsg, ctx1A.DownloadMessage.TargetPath, status, excp1);
            ctx1A.OrigSender.Tell(failmsg);
        }

        /// <summary>
        /// The FinishOff
        /// </summary>
        /// <param name="arg">The arg<see cref="Task{string}"/></param>
        /// <returns>The <see cref="GotContentMessage"/></returns>
        private GotContentMessage FinishOff(Task<string> arg) => new GotContentMessage(null);

        /// <summary>
        /// The FinishOff2
        /// </summary>
        /// <param name="arg">The arg<see cref="Task"/></param>
        /// <param name="state">The state<see cref="object"/></param>
        /// <returns>The <see cref="GotContentMessage"/></returns>
        private GotContentMessage FinishOff2(Task arg, object state) => throw new NotImplementedException();

        /// <summary>
        /// The GotDownloadPage
        /// </summary>
        /// <param name="gotmsg">The gotmsg<see cref="GotContentMessage"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool GotDownloadPage(GotContentMessage gotmsg)
        {
            var reqmsg = gotmsg.Message;
            _Log.Info($"DownloadActor.GotDownloadPage({reqmsg.Url}) Telling");
            Sender.Tell(new DownloadedMessage(reqmsg, reqmsg.TargetPath));       // TODO: pass thru real Sender (don't assume) !
            return true;                                        // show ActorSystem we handled message [expect next one immediately!]
        }

        /// <summary>
        /// The t_gotContent
        /// </summary>
        /// <param name="obj">The obj<see cref="Task{Task{GotContentMessage}}"/></param>
        private void t_gotContent(Task<Task<GotContentMessage>> obj) => throw new NotImplementedException();
        #endregion

        /// <summary>
        /// Defines the <see cref="Context1" />
        /// </summary>
        public class Context1
        {
            #region Fields
            /// <summary>
            /// Defines the DownloadMessage
            /// </summary>
            internal readonly DownloadMessage DownloadMessage;

            /// <summary>
            /// Defines the OrigSender
            /// </summary>
            internal readonly IActorRef OrigSender;
            #endregion

            #region Constructors
            /// <summary>
            /// Initializes a new instance of the <see cref="Context1"/> class.
            /// </summary>
            /// <param name="sender">The sender<see cref="IActorRef"/></param>
            /// <param name="downloadMessage">The downloadMessage<see cref="DownloadMessage"/></param>
            public Context1(IActorRef sender, DownloadMessage downloadMessage)
            {
                OrigSender = sender;
                DownloadMessage = downloadMessage;
            }
            #endregion
        }

        /// <summary>
        /// Defines the <see cref="GotContentMessage" />
        /// </summary>
        public class GotContentMessage
        {
            #region Fields
            /// <summary>
            /// Defines the Content
            /// </summary>
            public readonly string Content;

            /// <summary>
            /// Defines the Message
            /// </summary>
            public readonly DownloadMessage Message;
            #endregion

            #region Constructors
            /// <summary>
            /// Initializes a new instance of the <see cref="GotContentMessage"/> class.
            /// </summary>
            /// <param name="msg">The msg<see cref="DownloadMessage"/></param>
            public GotContentMessage(DownloadMessage msg)
            {
                Message = msg;
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="GotContentMessage"/> class.
            /// </summary>
            /// <param name="msg">The msg<see cref="DownloadMessage"/></param>
            /// <param name="content">The content<see cref="string"/></param>
            public GotContentMessage(DownloadMessage msg, string content) : this(msg)
            {
                Content = content;
            }
            #endregion
        }
    }
}
