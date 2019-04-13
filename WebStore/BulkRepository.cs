using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure;
using Infrastructure.Interfaces;
using Infrastructure.Models;

namespace WebStore
{
    public class BulkRepository : IRepository
    {
        /*
        These are done by EF using EfWip to enforce sequentiality
            Task<List<ContentTypeToExtn>> GetContentTypeToExtnsAsync()          READONLY so .AsNoTracking() 
            Task<List<WebPage>> GetWebPagesToDownloadAsync(int maxrows = 15);   DbSet<WebPage> restarted every cycle (batch of 15 requests) so won't grow huge
            Task<int> SaveChangesAsync();                                       AWAIT after #i so #i+1 can't catchup
        These are done by independent ADO using AdoWip to enforce sequentiality
            ctor trigger initiated
                SqlConnection Open + createCmd SqlCommand
            Task<bool> AddLinksAsync(WebPage webpage, IDictionary<string, string> linksDict);
                truncateCmd SqlCommand + _bulk SqlBulkCopy + sprocCmd SqlCommand
        */

        readonly Webstore.WebModel EfDomain;

        public enum Staging_enum        // columns within Staging table
        {
            Url,
            DraftFilespec,
            //  Filespec,               // NB this is omitted from Staging as unknown at link-extract time
            //  NeedDownload,           //  ditto no client-side sense if wanted
            NumberOfColumns
        }

        public enum Action_enum         // parameters to p_ActionWebPage sproc
        {
            PageId,
            Url,
            DraftFilespec,
            Filespec,
            NeedDownload,
            NumberOfColumns
        }

        enum EfWipEnum
        {
            Idle = 0,
            GetContentTypeToExtns,
            GetWebPagesToDownload,
            SaveChangesAsync
        }

        enum AdoWipEnum
        {
            Idle = 0,
            Open,
            CreateStaging,
            Truncate,
            Bulk,
            Action
        }
        /*
        public enum WebPage_enum        // WebPage tables columns as returned by GetWebPageByUrlAsync method
        {
            PageId,
            //  HostId,                 // not needed here so not in SELECT list
            Url,
            DraftFilespec,
            Filespec,
            NeedDownload
        }
        */

        const string TGTTABLE = "#WebpagesStaging";

        readonly string[] stagingNames = new string[] { "Url", "DraftFilespec" },
            stagingTypes = new string[] { "System.String", "System.String" };

        readonly SqlConnection _conn;               // SQL is single-threaded (forget MARS for this actor connection)

#if WIP
#pragma warning disable CS0414          // The field 'BulkRepository.LastEfCmd' is assigned but its value is never used.
        EfWipEnum LastEfCmd = EfWipEnum.Idle;       // most recent EF operation
        AdoWipEnum LastAdoCmd = AdoWipEnum.Idle;    // most recent ADO operation
#pragma warning restore CS0414          // The field 'BulkRepository.LastEfCmd' is assigned but its value is never used.
#endif
        // SINGLE concurrency with single spid doing ONE thing (forget MARS or equivalent). always fatal if any exception thrown (back to caller)
        Task EfWip = Task.FromResult<int>(12345);   // currently idle (cf. relates to GetContentTypeToExtnsAsync/GetWebPagesToDownloadAsync/SaveChangesAsync)
        Task AdoWip = Task.FromResult<int>(67890);  // currently idle
        readonly SqlCommand truncateCmd;
        readonly SqlBulkCopy _bulk;
        readonly SqlCommand addLinksCmd;
        readonly DbParameter[] p_ActionWebPageParams = new SqlParameter[]
                {
                    new SqlParameter("@PageId", SqlDbType.Int),
                    new SqlParameter("@Url", SqlDbType.NVarChar, WebPage.URLSIZE),
                    new SqlParameter("@DraftFilespec", SqlDbType.NVarChar, WebPage.FILESIZE),
                    new SqlParameter("@Filespec", SqlDbType.NVarChar, WebPage.FILESIZE),
                    new SqlParameter("@NeedDownload", SqlDbType.Bit)
                };

        const int CACHELEN = 2;                             // size of DataTable array (i.e. double-buffering)
        int ActiveData = 0;                                 // start with zero-th cache table
        readonly DataTable[] dataCaches = new DataTable[CACHELEN];  // array of DataTable instances (used round-robin cycle) convenient for debugging

        public BulkRepository(Webstore.WebModel dbctx)
        {
            Debug.Assert(stagingNames.Length == (int)Staging_enum.NumberOfColumns
                && stagingTypes.Length == (int)Staging_enum.NumberOfColumns,
                "Staging_enum metadata is incorrect");
            Debug.Assert(p_ActionWebPageParams.Length == (int)Action_enum.NumberOfColumns, "Action_enum metadata is incorrect");

            //EF component (single context, but SaveChangesAsync after request[I] can overlap request[I+1] doing its Downloader work in parallel
            EfDomain = dbctx;
            //  var ObjCtx = (EfDomain as IObjectContextAdapter).ObjectContext;
            //  ObjCtx.SavingChanges += OnSavingChanges;
            //LastEfCmd = EfWipEnum.Idle;

            // ADO component
            //LastAdoCmd = AdoWipEnum.Idle;
            var csb = new SqlConnectionStringBuilder(EfDomain.Database.Connection.ConnectionString)
            {
                ApplicationName = "DICKBULKSPROC",
                AsynchronousProcessing = false,
                ConnectRetryCount = 10,
                ConnectRetryInterval = 2,
                ConnectTimeout = 60,
                MultipleActiveResultSets = false,
                Pooling = false
            };
            _conn = new SqlConnection(csb.ConnectionString);       // independent so EF & ADO can free-run

            truncateCmd = new SqlCommand("truncate table " + TGTTABLE, _conn);
            addLinksCmd = new SqlCommand("exec dbo.p_ActionWebPage @PageId,@Url,@DraftFilespec,@Filespec,@NeedDownload", _conn);
            addLinksCmd.Parameters.AddRange(p_ActionWebPageParams);
            _bulk = new SqlBulkCopy(_conn) { DestinationTableName = TGTTABLE, BatchSize = 1000, BulkCopyTimeout = 15 };

#if WIP
            LastAdoCmd = AdoWipEnum.Open;
#endif
            AdoWip = (_conn.State != ConnectionState.Open)
                ? _conn.OpenAsync()                         // start the OPEN handshake async so we can do setup stuff in parallel
                : Task.FromResult<bool>(true);

            dataCaches[0] = MakeStagingTable();             // does not need _conn to be open yet run
            for (var i = 1; i < CACHELEN; i++)
            {
                dataCaches[i] = dataCaches[0].Clone();      // copy structure (but not data)
            }
            // wait for OPEN then create remote table on SQL asynchronously
            AdoWip = AdoWip.ContinueWith(t =>
            {
                t.BombIf();
                CreateStagingAsync();
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /*
        void OnSavingChanges(object sender, EventArgs e)
        {
            if (!(sender is ObjectContext ObjCtx))
            {
                return;
            }
            WebPageChanging(ObjCtx, EntityState.Deleted, "deleting");
            WebPageChanging(ObjCtx, EntityState.Added, "adding");
            WebPageChanging(ObjCtx, EntityState.Modified, "updating");
        }

        static void WebPageChanging(ObjectContext ObjCtx, EntityState changeType, string action)
        {
            Console.WriteLine($"{action}");
            foreach (var stateitem in ObjCtx.ObjectStateManager.GetObjectStateEntries(changeType))
            {
                if (!(stateitem.Entity is WebPage webpage))
                {
                    continue;
                }
                Console.WriteLine($"\t{webpage.Url}({webpage.PageId})");
            }
        }
        */

        /// <summary>
        ///     upload all links to SQL and execute dbo.p_ActionWebPage sproc
        /// </summary>
        /// <param name="webpage">
        ///     the dependent webpage (contains links to independent pages)
        /// </param>
        /// <param name="linksDict">
        ///     Dictionary of links found
        /// </param>
        /// <returns>
        ///     Task representing executing dbo.p_ActionWebPage sproc
        /// </returns>
        /// <remarks>
        ///     by using ContinueWith TAP methods there are not await markers so compiler warns CS1998, but this is intentional
        /// </remarks>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public async Task AddLinksAsync(WebPage webpage, IDictionary<string, string> linksDict)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            // 1.   simply return if no links found
            if (linksDict.Count == 0)
            {
                return;
            }

            // TODO: research if breaking out TRUNCATE here (instead of in 3-phase CW in Upload) would improve performance
            try
            {
                // 2.   populate the DataTable ready for upload
                var activeCache = dataCaches[ActiveData];
                activeCache.Clear();                         // hose all local data from any previous operation
                foreach (var lnk in linksDict)
                {
                    // Once a table has been created, use the NewRow to create a DataRow.
                    var row = activeCache.NewRow();

                    // Then add the new row to the collection.
                    row[(int)Staging_enum.Url] = lnk.Key;
                    row[(int)Staging_enum.DraftFilespec] = lnk.Value;
                    //row[(int)Staging_enum.NeedDownload] = 0;
                    activeCache.Rows.Add(row);

                    /*
                    for (var i = 0; i < activeCache.Columns.Count; i++)
                    {
                        Console.WriteLine("{0,-10}\t{1}", activeCache.Columns[i].ColumnName, row[i].ToString());
                    }
                    */
                }

                // 3.   actual upload
                //AdoWip.WaitBombIf();
                //AdoWip = _bulk.WriteToServerAsync(dataCaches[ActiveData]);   // upload current batch of {called[0]..[n-1] rows}
                var junk = Upload(webpage);
            }
            catch (Exception excp)
            {
                Console.WriteLine($"AddLinksAsync EXCEPTION:\t{excp}");
            }
        }

        Task CreateStagingAsync()                         // EF will OPEN() then initiate [but DON'T WAIT for] CREATE
        {
#if WIP
            LastAdoCmd = AdoWipEnum.CreateStaging;
#endif

#pragma warning disable CA2100      // "Review SQL queries for security vulnerabilities" has been done
            var createCmd = new SqlCommand(
                $"CREATE TABLE {TGTTABLE}\n" +
                $"(\t[Url]\t\tnvarchar({WebPage.URLSIZE})\tNOT NULL\tPRIMARY KEY,\n" +     // N.B. PKCI on Url for Staging (noPageId column here)
                $"\tDraftFilespec\tnvarchar({WebPage.FILESIZE})\tNULL)", _conn);
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
            return createCmd.ExecuteNonQueryAsync();
        }

        public Task<List<ContentTypeToExtn>> GetContentTypeToExtnsAsync()
        {
#if WIP
            LastEfCmd = EfWipEnum.GetContentTypeToExtns;
#endif
            EfWip.WaitBombIf();                               // wait for <nothing> to finish
            Task<List<ContentTypeToExtn>> rslt;
            EfWip = rslt = EfDomain.ContentTypeToExtns
                    .AsNoTracking()                             // read-only here
                    .Where(row => !string.IsNullOrEmpty(row.Template) && !string.IsNullOrEmpty(row.Extn))   // WHERE ((LEN([Extent1].[Template])) <> 0) AND ((LEN([Extent1].[Extn])) <> 0)
                    .OrderBy(row => row.Template)
                    .ToListAsync();
            return rslt;
        }

        //public WebPage GetWebPageById(int id) => EfDomain.WebPages.FirstOrDefault(row => row.PageId == id);
        //public WebPage GetWebPageByUrl(string url) => EfDomain.WebPages.FirstOrDefault(row => row.Url == url);
        //public IEnumerable<WebPage> GetWebPages() => EfDomain.WebPages;

        public Task<List<WebPage>> GetWebPagesToDownloadAsync(int maxrows = 15)
        {
            EfWip.WaitBombIf();                                 // wait for GetContentTypeToExtnsAsync / SaveChangesAsync to finish
            AdoWip.WaitBombIf();                                // *** TEMP ***

            //Debug.Assert(EfDomain.SaveChanges() == 0, "verify no unwritten changes in DbContext");

            // sadly this results in setting change-tracking for all rows to EnumEntityState.Deleted so don't enable !
            // imho best to live with the gradual growth of this collection - else incur high cost to cycle in a new DbContext as replacement
            //EfDomain.WebPages.Local.Clear();                  // toss all previous locally cached rows to improve search speed (cf. big-O!)

#if WIP
            LastEfCmd = EfWipEnum.GetWebPagesToDownload;
#endif
            Task<List<WebPage>> rslt;
            EfWip = rslt = EfDomain.WebPages
                .SqlQuery("exec p_ToDownload @Take=@TakeN", new SqlParameter("@TakeN", SqlDbType.Int) { Value = maxrows })
                .ToListAsync();                                 // solidify as List<WebPage> (i.e. no deferred execution)
            return rslt;
        }

        DataTable MakeStagingTable()
        {
            var stagingTable = new DataTable(TGTTABLE);         // Create a new DataTable

            // Add N column objects to the table
            for (var i = 0; i < stagingNames.Length; i++)
            {
                stagingTable.Columns.Add(
                    new DataColumn { DataType = Type.GetType(stagingTypes[i]), ColumnName = stagingNames[i] });
            }

            stagingTable.Columns[(int)Staging_enum.Url].Unique = true;  // Webpage.PageId is PK but in our staging table #WebPagesStaging.Url is PK

            // Create an array for DataColumn objects.
            stagingTable.PrimaryKey = new DataColumn[] { stagingTable.Columns[(int)Staging_enum.Url] };     // NB not PageId as 1..n-1 entries will be 0

            return stagingTable;                                // Return the new DataTable
        }

        public Task<int> SaveChangesAsync()
        {
#if WIP
            LastEfCmd = EfWipEnum.SaveChangesAsync;
#endif
            EfWip.WaitBombIf();                               // wait for previous GetContentTypeToExtnsAsync / SaveChangesAsync to finish
            Task<int> rslt;
            EfWip = rslt = EfDomain.SaveChangesAsync();
            return rslt;
        }

        public Task Upload(WebPage webpage)
        {
            // 1.   wait for previous operation (either CREATE TABLE or EXEC p_ActionWebPage) to complete, then perform TRUNCATE
            //await DoAsync(truncateCmd, truncateCmd.ExecuteNonQueryAsync).ConfigureAwait(true);    // trash all our #WebPagesStaging table data at Sql Server
            AdoWip.WaitBombIf();               // wait for previous operation (e.g. "CREATE TABLE #WebpagesStaging" or "exec dbo.p_ActionWebPage")
#if WIP
            LastAdoCmd = AdoWipEnum.Truncate;
            AdoWip = truncateCmd.ExecuteNonQueryAsync();                    // TRUNCATE TABLE must complete before we can do BULK INSERT

            // 2.    wait for TRUNCATE, then perform BULK INSERT and advance pointer so caller can continue
            AdoWip.WaitBombIf();                                            // wait for previous TRUNCATE operation to complete
            LastAdoCmd = AdoWipEnum.Bulk;
            AdoWip = _bulk.WriteToServerAsync(dataCaches[ActiveData]);      // upload current batch of {called[0]..[n-1] rows}
#else
            AdoWip = AdoWip
                .ContinueWith(t =>
                {
                    t.BombIf();                                             // .Wait() for any previous operation to complete (CREATE TABLE #WebpagesStaging or EXEC dbo.p_ActionWebPage)
                    truncateCmd.ExecuteNonQueryAsync();                     // TRUNCATE TABLE #WebpagesStaging
                }, TaskContinuationOptions.ExecuteSynchronously)
                .ContinueWith(t =>
                {
                    t.BombIf();                                             // wait for previous TRUNCATE operation to complete
                    _bulk.WriteToServerAsync(dataCaches[ActiveData]);       //  then upload current batch of {called[0]..[n-1] rows}
                }, TaskContinuationOptions.ExecuteSynchronously);
#endif

            // 3.   prepare sproc params, wait for INSERT BULK to complete, then exec dbo.p_ActionWebPage
            p_ActionWebPageParams[(int)Action_enum.PageId].Value = webpage.PageId;
            p_ActionWebPageParams[(int)Action_enum.Url].Value = webpage.Url;
            p_ActionWebPageParams[(int)Action_enum.DraftFilespec].Value = webpage.DraftFilespec;
            p_ActionWebPageParams[(int)Action_enum.Filespec].Value = webpage.Filespec;
            p_ActionWebPageParams[(int)Action_enum.NeedDownload].Value = webpage.NeedDownload;

            ActiveData = ++ActiveData % CACHELEN;                   // round-robin advance to next DataTable
            dataCaches[ActiveData].Clear();                         // hose all local data from any previous operation
                                                                    // the caller is now free to re-use this zeroed dataCache (i.e. invoke AddDataRow)

#if WIP
            LastAdoCmd = AdoWipEnum.Action;
#endif
            AdoWip.WaitBombIf();                                    // .Wait for previous BULK INSERT operation to complete
            Task<int> sq;
            AdoWip = sq = addLinksCmd.ExecuteNonQueryAsync();       // import #WebStaging data into WebPages and Depends tables
            //var qty = sq.Result;                                    // async until completed ***** TEMPORARY *****

            // drop out here so task wrapper completes, thus allows caller to populate next buffer in the ring (whilst SqlServer runs dbo.p_ActionWebPage sproc)
            return AdoWip;
        }
    }
}
