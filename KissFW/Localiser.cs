using System.Collections.Generic;
using System.IO;
using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;

namespace KissFW
{
    public class Localiser
    {

        readonly IRepository Dataserver;
        readonly IHttpParser Httpserver;
        readonly string HtmlPath;               // subfolder to read *.html
        readonly string BackupPath;             //  ditto to write revised (localised) *.html
        public Localiser(IRepository dataserver, IHttpParser httpserver, string htmlPath, string backupPath=null)
        {
            Dataserver = dataserver;
            Httpserver = httpserver;
            HtmlPath = htmlPath;
            BackupPath = backupPath;
        }

        /*
        public enum TxformEnum { setBackup, retire, saveNew, recall, fin }
        public struct TranslateState
        {
            public readonly TxformEnum CurrentState;
            public readonly string Description;
            public readonly TxformEnum NextGood;
            public readonly TxformEnum NextBad;
            public readonly string BadMsg;

            public TranslateState(TxformEnum currentState, string description, TxformEnum nextGood, TxformEnum nextBad, string BadMessage = "genericfail")
            {
                CurrentState = currentState;
                Description = description;
                NextGood = nextGood;
                NextBad = nextBad;
                BadMsg = BadMessage;
            }
        }

        TranslateState[] stateData = {          // table-driven state machine (code has communal try-catch handling)
            new TranslateState(TxformEnum.setBackup, "determine free backup", TxformEnum.retire,  TxformEnum.setBackup),
            new TranslateState(TxformEnum.retire,    "original to backup",    TxformEnum.saveNew, TxformEnum.setBackup, "Translate: failed to rename {0}\tto\t{1}\n\t{2}"),
            new TranslateState(TxformEnum.saveNew,   "save revised hdoc",     TxformEnum.fin,     TxformEnum.recall,"Translate: failed to save revised {1}"),
            new TranslateState(TxformEnum.recall,    "restore from backup",   TxformEnum.fin,     TxformEnum.fin, "Translate failed to recover {1}\tto\t{0}\n\t{2}")
        };
        */

        internal bool Translate(WebPage webpage)
        {

            var mydict = new Dictionary<string, string>();
            foreach (var dad in webpage.ConsumeFrom)
            {
                var fs = dad.Filespec;
                if (!string.IsNullOrWhiteSpace(fs))
                {
                    mydict.Add(dad.Url, fs);
                }
            }
            var origfs = webpage.Filespec;
            Httpserver.LoadFromFile(webpage.Url, origfs);
            var changedLinks = Httpserver.ReworkLinks(origfs, mydict);
            if (!changedLinks)
            {
                return false;        // no link replacement achieved
            }

            var newFilespec =       // htmldir + Path.DirectorySeparatorChar + Path.GetRandomFileName()
                Path.Combine(HtmlPath, Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + ".html");
            Httpserver.SaveFile(newFilespec);

            var backfs = Path.Combine(BackupPath, Path.GetFileName(origfs));
            var result = Utils.RetireFile(origfs, backfs, newFilespec);

            return result;

            /*
            var current = TxformEnum.setBackup;
            var retries = 5;
            while (--retries > 0)
            {
                var curr = stateData[(int)current];
                try
                {
                    switch (current)
                    {
                        case TxformEnum.setBackup:          // attempt to invent backfs (i.e. file that doesn't already exist)
                            if (File.Exists(backfs))
                            {
                                System.Console.WriteLine($"Translate: {backfs} already exists, will generate another");
                                backfs = Path.Combine(BackupPath, Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + "." + Path.GetExtension(origfs));
                            }
                            else
                            {
                                retries = 5;                        // top-up retrycount for subsequent steps
                            }
                            break;
                        case TxformEnum.retire:                     // attempt to move origfs to backfs
                            File.Move(origfs, backfs);
                            break;
                        case TxformEnum.saveNew:                    // attempt to save revised .html file
                            Httpserver.SaveFile(origfs);
                            return Task.FromResult<bool>(true);     // normal success
                        case TxformEnum.recall:                     // restore backfs to origfs
                            retries = 0;                            // ensure any catch from here aborts (otherwise could delete origfs file)
                            if (File.Exists(origfs) && File.Exists(backfs))
                            {
                                File.Delete(origfs);
                                File.Move(backfs, origfs);
                                System.Console.WriteLine($"Translate restored {backfs} to {origfs}");
                            }
                            goto default;

                        case TxformEnum.fin:
                        default:
                            return Task.FromResult<bool>(false);    // hopefully origfs back as it was, but no link replacement achieved
                    }
                }
                catch (System.Exception excp)
                {
                    System.Console.WriteLine(curr.BadMsg, current, origfs, backfs, excp.Message);
                    current = curr.NextBad;
                }
                return Task.FromResult<bool>(false);                // steps exhausted
            }
            if (retries <= 0)
            {
                System.Console.WriteLine($"Translate cannot preserve({origfs}");
                changedLinks = false;
            }
            return Task.FromResult<bool>(changedLinks);
            */
        }
    }
}
