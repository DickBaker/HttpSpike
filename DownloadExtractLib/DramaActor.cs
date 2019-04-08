using System;
using System.Collections.Generic;
using Akka.Actor;
using DownloadExtractLib.Messages;

namespace DownloadExtractLib
{
    public class DramaActor : ReceiveActor
    {
        readonly Dictionary<string, string> Url2FileDict = new Dictionary<string, string>();
        readonly string OutFolder;
        readonly IActorRef DownloadCoordinator;

        public DramaActor(IActorRef downloadCoordinatorActor, string outFolder)
        {
            DownloadCoordinator = downloadCoordinatorActor ?? throw new InvalidOperationException("Must specify DownloadCoordinatorActor on DramaActor ctor");
            outFolder = outFolder?.Trim();
            if (outFolder == null)
            {
                throw new InvalidOperationException("Must specify target directory on creation");
            }

            OutFolder = (outFolder[outFolder.Length - 1] == '\\') ? outFolder : outFolder + '\\';
            Receive<AddMessage>(AddHandler);
        }

        bool AddHandler(AddMessage addmsg)
        {
            string fileName;
            if (!Url2FileDict.ContainsKey(addmsg.Url))
            {
                fileName = addmsg.FileName;
                if (!Url2FileDict.ContainsValue(fileName))          // name clash (different Url already claimed same name) ?
                {
                    fileName = (new Guid()).ToString();             // yes, so invent alias fiilename instead
                }
                Url2FileDict.Add(addmsg.Url, fileName);             // record this mapping
            }
            else
            {
                fileName = Url2FileDict[addmsg.Url];                // this file already registered [avoid multiple downloads]
            }
            return true;
        }
    }
}