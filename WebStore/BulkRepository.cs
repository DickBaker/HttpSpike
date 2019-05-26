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
using Polly;

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
            NumberOfColumns
        }

        enum EfWipEnum                  // states for async ADO activity
        {
            Idle = 0,
            AddWebPage,
            GetContentTypeToExtns,
            GetWebPageByUrl,
            GetWebPagesToDownload,
            GetWebPagesToLocalise,
            SaveChangesAsync
        }

        enum AdoWipEnum                 // states for async EF activity
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

        const string TGTTABLE = "#WebpagesStaging", COLLATION = "COLLATE SQL_Latin1_General_CP1_CI_AS";

        readonly string[] stagingNames = { "Url", "DraftFilespec" },
            stagingTypes = { "System.String", "System.String" };

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
                new SqlParameter("@Url", SqlDbType.NVarChar, WebPage.URLSIZE)
            };

        const int CACHELEN = 2;                             // size of DataTable array (i.e. double-buffering)
        int ActiveData = 0;                                 // start with zero-th cache table
        readonly DataTable[] dataCaches = new DataTable[CACHELEN];  // array of DataTable instances (used round-robin cycle) convenient for debugging
        readonly IAsyncPolicy Policy;
        WebPage ActionPage = null;

        public BulkRepository(Webstore.WebModel dbctx, IAsyncPolicy retryPolicy)
        {
            Policy = retryPolicy;
            Debug.Assert(stagingNames.Length == (int)Staging_enum.NumberOfColumns
                && stagingTypes.Length == (int)Staging_enum.NumberOfColumns,
                "Staging_enum metadata is incorrect");
            Debug.Assert(p_ActionWebPageParams.Length == (int)Action_enum.NumberOfColumns, "Action_enum metadata is incorrect");

            //EF component (single context, but SaveChangesAsync after request[I] can overlap request[I+1] doing its Downloader work in parallel
            EfDomain = dbctx;
            //  var ObjCtx = (EfDomain as IObjectContextAdapter).ObjectContext;
            //  ObjCtx.SavingChanges += OnSavingChanges;

            // ADO component
            var csb = new SqlConnectionStringBuilder(EfDomain.Database.Connection.ConnectionString)
            {
                ApplicationName = "DICKBULKSPROC",
                // AsynchronousProcessing = false,
                ConnectRetryCount = 10,
                ConnectRetryInterval = 2,
                ConnectTimeout = 60,
                MultipleActiveResultSets = false,
                Pooling = false
            };
            _conn = new SqlConnection(csb.ConnectionString);       // independent SPID so EF & ADO can free-run

            truncateCmd = new SqlCommand("truncate table " + TGTTABLE, _conn);
            addLinksCmd = new SqlCommand("exec dbo.p_ActionWebPage @PageId,@Url", _conn);
            addLinksCmd.Parameters.AddRange(p_ActionWebPageParams);
            _bulk = new SqlBulkCopy(_conn) { DestinationTableName = TGTTABLE, BatchSize = 500, BulkCopyTimeout = 45 };

#if WIP
            LastAdoCmd = AdoWipEnum.Open;
#endif
            AdoWip = (_conn.State != ConnectionState.Open)
                ? Policy.ExecuteAsync(() => _conn.OpenAsync())  // start the OPEN handshake async so we can do setup stuff in parallel
                : Task.FromResult<bool>(true);

            dataCaches[0] = MakeStagingTable();                 // does not need _conn to be open yet run
            for (var i = 1; i < CACHELEN; i++)
            {
                dataCaches[i] = dataCaches[0].Clone();          // copy structure (but not data)
            }
            // wait for OPEN then create remote table on SQL asynchronously
            AdoWip = AdoWip.ContinueWith(
                t => Policy.ExecuteAsync(() => CreateStagingAsync()),
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously);
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
                foreach (var kvp in linksDict)
                {
                    // Once a table has been created, use the NewRow to create a DataRow.
                    var row = activeCache.NewRow();

                    // Then add the new row to the collection.
                    row[(int)Staging_enum.Url] = kvp.Key;
                    row[(int)Staging_enum.DraftFilespec] = kvp.Value;
                    //row[(int)Staging_enum.NeedDownload] = 0;
                    activeCache.Rows.Add(row);

                    /*
                    for (var i = 0; i < activeCache.Columns.Count; i++)
                    {
                        Console.WriteLine("{0,-10}\t{1}", activeCache.Columns[i].ColumnName, row[i].ToString());
                    }
                    */
                }

                // 3.   upload now but defer exec sproc to after SaveChanges
                Debug.Assert(ActionPage == null, "previous deferment not yet actioned");
                ActionPage = webpage;
                var junk = Upload(webpage);
            }
            catch (Exception excp)
            {
                Console.WriteLine($"AddLinksAsync EXCEPTION:\t{excp}");
            }
        }

        public WebPage AddWebPage(WebPage newpage)
        {
            EfWip.WaitBombIf();                         // wait for <*> to finish
#if WIP
            LastEfCmd = EfWipEnum.AddWebPage;
#endif
            var wp = EfDomain.WebPages.Add(newpage);    // Local only (no db net work)
            return wp;
        }
        Task CreateStagingAsync()                       // EF will OPEN() then initiate [but DON'T WAIT for] CREATE
        {
#if WIP
            LastAdoCmd = AdoWipEnum.CreateStaging;
#endif

#pragma warning disable CA2100      // "Review SQL queries for security vulnerabilities" has been done
            var createCmd = new SqlCommand(
                $"DROP TABLE IF EXISTS {TGTTABLE};\n" +                                 // drop any pre-existing table (allow Polly to be idempotent)
                $"CREATE TABLE {TGTTABLE}\n" +
                $"(\t[Url]\t\tnvarchar({WebPage.URLSIZE})\t{COLLATION}\tNOT NULL\tPRIMARY KEY,\n" +  // N.B. PKCI on Url for Staging (noPageId column here)
                $"\tDraftFilespec\tnvarchar({WebPage.FILESIZE})\t{COLLATION}\tNULL)", _conn);
#pragma warning restore CA2100 // Review SQL queries for security vulnerabilities
            return createCmd.ExecuteNonQueryAsync();
        }

        public Task<List<ContentTypeToExtn>> GetContentTypeToExtnsAsync()
        {
            EfWip.WaitBombIf();                                 // wait for <nothing> to finish
#if WIP
            LastEfCmd = EfWipEnum.GetContentTypeToExtns;
#endif
            Task<List<ContentTypeToExtn>> rslt;
            EfWip = rslt = EfDomain.ContentTypeToExtns
                    //.AsNoTracking()                             // read-only here
                    //.Where(row => !string.IsNullOrEmpty(row.Template) && !string.IsNullOrEmpty(row.Extn))   // WHERE ((LEN([Extent1].[Template])) <> 0) AND ((LEN([Extent1].[Extn])) <> 0)
                    .OrderBy(row => row.Template)
                    .ToListAsync();

            //EfWip.WaitBombIf();                                 // wait for <***> to finish
            //var xxx = EfDomain.ContentTypeToExtns.ToList();

            return rslt;
        }

        //public WebPage GetWebPageById(int id) => EfDomain.WebPages.FirstOrDefault(row => row.PageId == id);

        public Task<WebPage> GetWebPageByUrlAsync(string url)
        {
            EfWip.WaitBombIf();                                 // wait for <***> to finish
#if WIP
            LastEfCmd = EfWipEnum.GetWebPageByUrl;
#endif
            var wplocal = EfDomain.WebPages.Local.FirstOrDefault(row => row.Url == url);
            if (wplocal != null)
            {
                return Task.FromResult<WebPage>(wplocal);       // found locally (sync)
            }
            Task<WebPage> rslt;
            EfWip = rslt = EfDomain.WebPages.FirstOrDefaultAsync(row => row.Url == url);    // async roundtrip to db
            return rslt;
        }

        //public IEnumerable<WebPage> GetWebPages() => EfDomain.WebPages;

        public Task<List<WebPage>> GetWebPagesToDownloadAsync(int maxrows = 15)
        {
            EfWip.WaitBombIf();                                 // wait for GetContentTypeToExtnsAsync / SaveChangesAsync to finish
            //AdoWip.WaitBombIf();                                // *** TEMP ***

            // sadly this results in setting change-tracking for all rows to EnumEntityState.Deleted so don't enable !
            // imho best to live with the gradual growth of this collection - else incur high cost to cycle in a new DbContext as replacement
            //EfDomain.WebPages.Local.Clear();                  // toss all previous locally cached rows to improve search speed (cf. big-O!)

#if WIP
            LastEfCmd = EfWipEnum.GetWebPagesToDownload;
#endif
            var takeprm = new SqlParameter("@TakeN", SqlDbType.Int)  // have to recreate every time (presumably as EF invents new SqlCommand) to avoid
            { Value = maxrows };                                    //  "The SqlParameter is already contained by another SqlParameterCollection" error
            var N = EfDomain.WebPages.Local.Count;
            Task<List<WebPage>> rslt;
            EfWip = rslt = EfDomain.WebPages
                .SqlQuery("exec p_ToDownload @Take=@TakeN", takeprm)
                .ToListAsync();                                     // solidify as List<WebPage> (i.e. no deferred execution), and caller will await to get # requested
            return rslt;
        }

        public Task<List<WebPage>> GetWebPagesToLocaliseAsync(int maxrows = 15)
        {
            EfWip.WaitBombIf();                                     // wait for GetContentTypeToExtnsAsync / SaveChangesAsync to finish

#if WIP
            LastEfCmd = EfWipEnum.GetWebPagesToLocalise;
#endif
            var takeprm = new SqlParameter("@TakeN", SqlDbType.Int)  // have to recreate every time (presumably as EF invents new SqlCommand) to avoid
            { Value = maxrows };                                    //  "The SqlParameter is already contained by another SqlParameterCollection" error
            Task<List<WebPage>> rslt;
            EfWip = rslt = EfDomain.WebPages
                .SqlQuery("exec dbo.p_ToLocalise @Take=@TakeN", takeprm)
                .ToListAsync();                                     // solidify as List<WebPage> (i.e. no deferred execution), and caller will await to get # requested

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
            EfWip.WaitBombIf();                         // wait for any previous async SQL traffic by EF to finish
            AdoWip.WaitBombIf();                        // ditto wait for any previous async p_ActionWebPage sproc operation to complete [avoid internal deadlocks]
#if WIP
            LastEfCmd = EfWipEnum.SaveChangesAsync;
#endif
            Task<int> rslt;
            EfWip = rslt = EfDomain.SaveChangesAsync();

            // 3.   previous upload, but exec sproc deferred to after SaveChanges, i.e. now
            if (ActionPage != null)
            {
                EfWip.WaitBombIf();                 // wait for repository's SaveChangesAsync to finish (e.g. to persist new redirected WebPage)
                SaveLinks(ActionPage);              // actually invoke p_ActionWebPage sproc
                ActionPage = null;
                AdoWip.WaitBombIf();                // wait for parallel ADO to quiesce (i.e. p_Action sproc)
            }

            return rslt;                            // caller can await to get integer rowcount
        }

        /// <summary>
        ///     execute the p_ActionWebPage sproc to upsert the data in #Staging table
        /// </summary>
        ///     Upload method has already uploaded the data into #Staging table, but deferred until after SaveChanges completes
        ///      to ensure redirect WebPage has been persisted (i.e. nz PageId)
        /// <remarks>
        ///     1.  .Wait for previous BULK INSERT operation to complete
        ///     2.  prepare sproc params (now the PageId value has been assigned by Sql+EF)
        ///     3.  exec dbo.p_ActionWebPage sproc
        /// </remarks>
        void SaveLinks(WebPage webpage)
        {
            AdoWip.WaitBombIf();                        // wait for parallel ADO to quiesce (e.g. BULKINSERT)
            p_ActionWebPageParams[(int)Action_enum.PageId].Value = webpage.PageId;
            p_ActionWebPageParams[(int)Action_enum.Url].Value = webpage.Url;

#if WIP
            LastAdoCmd = AdoWipEnum.Action;
#endif
            // code is idempotent safe as sproc checks if Depends row pre-exists
            AdoWip = Policy.ExecuteAsync(() => addLinksCmd.ExecuteNonQueryAsync()); // import #WebStaging data into WebPages and Depends tables

            //AdoWip.WaitBombIf();                      // wait for parallel ADO to quiesce (e.g. p_ActionWebPage sproc) TODO: double-check ?
            // caller can now populate next buffer in the ring (whilst SqlServer runs sproc)
        }

        public int SaveChanges()
        {
            EfWip.WaitBombIf();                         // wait for any previous async SQL traffic by EF to finish
            AdoWip.WaitBombIf();                        // ditto wait for any previous async p_ActionWebPage sproc operation to complete [avoid internal deadlocks]
#if WIP
            LastEfCmd = EfWipEnum.SaveChangesAsync;     // although method not Async, it refreshes latest activity
#endif
            return EfDomain.SaveChanges();              // N.B. EF has its own retry and transaction wrappers
        }

        public Task Upload(WebPage webpage)
        {
            // 1.   wait for previous operation (either CREATE TABLE or EXEC p_ActionWebPage) to complete, then perform TRUNCATE
            //await DoAsync(truncateCmd, truncateCmd.ExecuteNonQueryAsync).ConfigureAwait(true);    // trash all our #WebPagesStaging table data at Sql Server
            AdoWip.WaitBombIf();               // wait for previous operation (e.g. "CREATE TABLE #WebpagesStaging" or "exec dbo.p_ActionWebPage")
#if WIP
            LastAdoCmd = AdoWipEnum.Truncate;
            AdoWip = Policy.ExecuteAsync(() => truncateCmd.ExecuteNonQueryAsync()); // TRUNCATE TABLE must complete before we can do BULK INSERT

            // 2.    wait for TRUNCATE, then perform BULK INSERT to upload into #Staging
            AdoWip.WaitBombIf();                                            // wait for previous TRUNCATE operation to complete
            LastAdoCmd = AdoWipEnum.Bulk;
            AdoWip = _bulk.WriteToServerAsync(dataCaches[ActiveData]);      // upload current batch [but no Polly retry as not idempotent and atomic]
#else
            AdoWip = Policy.ExecuteAsync(() =>                      // idempotent : if BULKINSERT fails then re-reun TRUNCATE
                truncateCmd.ExecuteNonQueryAsync()                  // TRUNCATE TABLE #WebpagesStaging
                    .ContinueWith(t => _bulk.WriteToServerAsync(dataCaches[ActiveData]), TaskContinuationOptions.OnlyOnRanToCompletion));
#endif

            // 3.   advance pointer so caller can continue
            ActiveData = ++ActiveData % CACHELEN;                   // round-robin advance to next DataTable
            dataCaches[ActiveData].Clear();                         // hose all local data from any previous operation
                                                                    // the caller is now free to re-use this zeroed dataCache (i.e. invoke AddDataRow)

            return AdoWip;
        }
    }
}
