using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Infrastructure.Interfaces;
using Infrastructure.Models;

namespace WebStore
{
    public class BulkRepository : IRepository
    {
        private readonly Webstore.WebModel EfDomain;

        public enum Staging_enum        // columns within Staging table
        {
            Url,
            DraftFilespec,
            //  Filespec,               // NB this is omitted from Staging as unknown at link-extract time
            NeedDownload,
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

        enum WipEnum
        {
            Idle = 0,
            CreateStaging,
            GetContentTypeToExtns,
            GetWebPagesToDownload,
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

        readonly string[] stagingNames = new string[] { "Url", "DraftFilespec", "NeedDownload" },
            stagingTypes = new string[] { "System.String", "System.String", "System.Boolean" };

        readonly SqlConnection _conn;            // SQL is single-threaded (forget MARS for this actor connection)

        readonly DbParameter[] p_ActionWebPageParams = new SqlParameter[]
                {
                    new SqlParameter("@PageId", SqlDbType.Int),
                    new SqlParameter("@Url", SqlDbType.NVarChar, WebPage.URLSIZE),
                    new SqlParameter("@DraftFilespec", SqlDbType.NVarChar, WebPage.FILESIZE),
                    new SqlParameter("@Filespec", SqlDbType.NVarChar, WebPage.FILESIZE),
                    new SqlParameter("@NeedDownload", SqlDbType.Bit)
                };

#if DEBUG
        WipEnum LastCmd = WipEnum.Idle;                     // most recent SQL operation
#endif
        // SINGLE concurrency with single spid doing ONE thing (forget MARS or equivalent)
        Task SqlInProgress;                                 // currently idle
        readonly SqlBulkCopy _bulk;

        const int CACHELEN = 2;                             // size of DataTable array (i.e. double-buffering)
        int ActiveData = 0;                                 // start with zero-th cache table
        readonly DataTable[] dataCaches = new DataTable[CACHELEN];  // array of DataTable instances (used round-robin cycle) convenient for debugging

        public BulkRepository(Webstore.WebModel dbctx)
        {
            Debug.Assert(stagingNames.Length == (int)Staging_enum.NumberOfColumns
                && stagingTypes.Length == (int)Staging_enum.NumberOfColumns,
                "Staging_enum metadata is incorrect");
            Debug.Assert(p_ActionWebPageParams.Length == (int)Action_enum.NumberOfColumns, "Action_enum metadata is incorrect");

            EfDomain = dbctx;
            //  var ObjCtx = (EfDomain as IObjectContextAdapter).ObjectContext;
            //  ObjCtx.SavingChanges += OnSavingChanges;

            SqlInProgress = CreateStagingAsync();           // OPEN() then create remote table on SQL asynchronously

            _conn = (SqlConnection)EfDomain.Database.Connection;
            _bulk = new SqlBulkCopy(_conn) { DestinationTableName = TGTTABLE };

            dataCaches[0] = MakeStagingTable();             // does not need _conn to be open yet run

            for (var i = 1; i < CACHELEN; i++)
            {
                dataCaches[i] = dataCaches[0].Clone();      // copy structure (but not data)
            }
        }

        /*
        private void OnSavingChanges(object sender, EventArgs e)
        {
            if (!(sender is ObjectContext ObjCtx))
            {
                return;
            }
            WebPageChanging(ObjCtx, EntityState.Deleted, "deleting");
            WebPageChanging(ObjCtx, EntityState.Added, "adding");
            WebPageChanging(ObjCtx, EntityState.Modified, "updating");
        }

        private static void WebPageChanging(ObjectContext ObjCtx, EntityState changeType, string action)
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

        public async Task<bool> AddLinksAsync(WebPage webpage, IDictionary<string, string> linksDict)
        {
            // 1.   simply return if no links found
            if (linksDict.Count == 0)
            {
                return await Task.FromResult<bool>(true);
            }

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
                row[(int)Staging_enum.NeedDownload] = 0;
                activeCache.Rows.Add(row);

                for (var i = 0; i < activeCache.Columns.Count; i++)
                {
                    Console.WriteLine("{0,-10}\t{1}", activeCache.Columns[i].ColumnName, row[i].ToString());
                }
            }
            // 3.   actual upload
            SqlInProgress.Wait();
            if (_conn.State != ConnectionState.Open)
            {
                _conn.Open();
            }
            await _bulk.WriteToServerAsync(dataCaches[ActiveData]).ConfigureAwait(true);  // upload current batch of {called[0]..[n-1] rows}
            return true;
        }

        public Task<List<ContentTypeToExtn>> GetContentTypeToExtnsAsync()
        {
            SqlInProgress.Wait();                               // wait for CREATE TABLE to finish
#if DEBUG
            LastCmd = WipEnum.GetContentTypeToExtns;
#endif
            Task<List<ContentTypeToExtn>> rslt;
            SqlInProgress = rslt =
                EfDomain.ContentTypeToExtns
                    .AsNoTracking()                             // read-only here
                    .Where(row => !string.IsNullOrEmpty(row.Template) && !string.IsNullOrEmpty(row.Extn))   // WHERE ((LEN([Extent1].[Template])) <> 0) AND ((LEN([Extent1].[Extn])) <> 0)
                    .OrderBy(row => row.Template)
                    .ToListAsync();
            return rslt;
        }

        public WebPage GetWebPageById(int id) => EfDomain.WebPages.FirstOrDefault(row => row.PageId == id);
        //public WebPage GetWebPageByUrl(string url) => EfDomain.WebPages.FirstOrDefault(row => row.Url == url);
        //public IEnumerable<WebPage> GetWebPages() => EfDomain.WebPages;

        public Task<List<WebPage>> GetWebPagesToDownloadAsync(int maxrows = 15)
        {
            SqlInProgress.Wait();                               // wait for GetContentTypeToExtnsAsync to finish
#if DEBUG
            LastCmd = WipEnum.GetWebPagesToDownload;
#endif
            Task<List<WebPage>> rslt;
            SqlInProgress = rslt = EfDomain.WebPages
                .SqlQuery("exec p_ToDownload @Take=@TakeN", new SqlParameter("@TakeN", SqlDbType.Int) { Value = maxrows })
                .ToListAsync();                                 // solidify as List<WebPage> (i.e. no deferred execution)
            return rslt;
        }

        /*
        public WebPage PutWebPage(WebPage webpage)
        {
            if (EfDomain.WebPages.Local.Count == 0)             // read entire table on first call
            {
                var webPages = EfDomain.WebPages.ToList();
                Console.WriteLine($"pagecnt={EfDomain.WebPages.Local.Count}");
            }

            // try local cache before external trip to DB
            var wptemp =
                //EfDomain.WebPages.Local.FirstOrDefault(row => row.Url.Equals(webpage.Url, StringComparison.InvariantCultureIgnoreCase))
                EfDomain.WebPages.FirstOrDefault(row => row.Url.Equals(webpage.Url, StringComparison.InvariantCultureIgnoreCase));
            if (wptemp == null)
            {
                return EfDomain.WebPages.Add(webpage);
            }
            if (!webpage.DraftFilespec.Equals(wptemp.DraftFilespec, StringComparison.InvariantCultureIgnoreCase))
            {
                if (wptemp.DraftFilespec == null || wptemp.DraftFilespec == "unknown")
                {
                    Console.WriteLine($"PutHost[DraftFilespec] {wptemp.DraftFilespec} -> {webpage.Filespec}");
                    wptemp.DraftFilespec = webpage.DraftFilespec;
                }
                else
                {
                    Console.WriteLine($"===> check {wptemp.DraftFilespec} -> {webpage.DraftFilespec}");
                }
            }
            if (webpage.Filespec != null && wptemp.Filespec != webpage.Filespec)
            {
                Console.WriteLine($"PutHost[Filespec] {wptemp.Filespec} -> {webpage.Filespec}");
                wptemp.Filespec = webpage.Filespec;
            }
            return wptemp;
        }
        */

        public Task<int> SaveChangesAsync() => EfDomain.SaveChangesAsync();

        Task CreateStagingAsync()                         // EF will OPEN() then initiate [but DON'T WAIT for] CREATE
        {
#if DEBUG
            LastCmd = WipEnum.CreateStaging;
#endif
            return EfDomain.Database.ExecuteSqlCommandAsync(
                $"CREATE TABLE {TGTTABLE}\n" +
                $"(\t[Url]\t\tnvarchar({WebPage.URLSIZE})\tNOT NULL\tPRIMARY KEY,\n" +     // N.B. PKCI on Url for Staging (noPageId column here)
                $"\tDraftFilespec\tnvarchar({WebPage.FILESIZE})\tNULL,\n" +
                $"\tNeedDownload\tbit\t\tNULL)");
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

        public async Task Upload(WebPage webpage)
        {
            // 1.   wait for previous operation (either CREATE TABLE or EXEC p_ActionWebPage) to complete, then perform TRUNCATE
            //await DoAsync(truncateCmd, truncateCmd.ExecuteNonQueryAsync).ConfigureAwait(true);    // trash all our #WebPagesStaging table data at Sql Server
            SqlInProgress.Wait();               // wait for previous operation (e.g. "CREATE TABLE #WebpagesStaging" or "exec dbo.p_ActionWebPage"). fatal if exception thrown (back to caller)
#if DEBUG
            LastCmd = WipEnum.Truncate;
#endif
            await EfDomain.Database.ExecuteSqlCommandAsync("TRUNCATE TABLE " + TGTTABLE);           // TRUNCATE TABLE must complete before we can do BULK INSERT

            // 2.    wait for TRUNCATE, then perform BULK INSERT and advance pointer so caller can continue
            SqlInProgress.Wait();               // wait for previous TRUNCATE operation. fatal if exception thrown (back to caller)
#if DEBUG
            LastCmd = WipEnum.Bulk;
#endif

            if (_conn.State!=ConnectionState.Open)
            {
                _conn.Open();
            }
            await _bulk.WriteToServerAsync(dataCaches[ActiveData]);   // upload current batch of {called[0]..[n-1] rows}

            // 3.   prepare sproc params, wait for INSERT BULK to complete, then exec dbo.p_ActionWebPage
            p_ActionWebPageParams[(int)Action_enum.PageId].Value = webpage.PageId;
            p_ActionWebPageParams[(int)Action_enum.Url].Value = webpage.Url;
            p_ActionWebPageParams[(int)Action_enum.DraftFilespec].Value = webpage.DraftFilespec;
            p_ActionWebPageParams[(int)Action_enum.Filespec].Value = webpage.Filespec;
            p_ActionWebPageParams[(int)Action_enum.NeedDownload].Value = webpage.NeedDownload;

            ActiveData = ++ActiveData % CACHELEN;                   // round-robin advance to next DataTable
            //dataCaches[ActiveData].Clear();                       // hose all local data from any previous operation
            //                                                      // the caller is now free to re-use this zeroed dataCache (i.e. invoke AddDataRow)

#if DEBUG
            LastCmd = WipEnum.Action;
#endif
            var sq = EfDomain.Database.ExecuteSqlCommandAsync("exec dbo.p_ActionWebPage", p_ActionWebPageParams);
            var qty = sq.Result;                                    // async until completed

            // await DoAsync(sprocCmd, sprocCmd.ExecuteNonQueryAsync).ConfigureAwait(true); // import #WebStaging data into WebPages and Depends tables

            // drop out here so task wrapper completes, thus allows caller to populate next buffer in the ring (whilst SqlServer runs dbo.p_ActionWebPage sproc)
        }
    }
}
