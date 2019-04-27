

CREATE PROCEDURE [dbo].[p_ToDownload] 
/*
PURPOSE
	process newly-uploaded batch of rows

HISTORY
	20190320 dbaker created
	20190417 dbaker	ignore Filespec when considering downloadability (may want re-download)
	20190419 dbaker	remove HostId and add NeedLocalise column from resultset
	20190423 dbaker	recode NeedDownload, NeedLocalise bit to Download, Localise byte to enhance valid states

EXAMPLES
	exec dbo.p_ToDownload
	exec dbo.p_ToDownload	'http%://%DICK.com%'
	exec dbo.p_ToDownload	'http%://%DICK.com%', 'html', @Take = 20
	exec dbo.p_ToDownload	@Extn = 'json', @Take = 15

NOTES
1.	Agents table is indexed by the SPID of the calling client (assumed to be an agent)
2.	dbo.Downloading table keeps short-term memory of the batch (of 10) requests given to the agent (previous call)
3.	if any downloaded failed this last interval, entries are Retries++ and ineligible as download candidates for 20 minutes
4.	once agent[i] have been given a batch of Urls the majority HostId is remembered and takes precedence next call (encourages sticky cookies etc)
5.	then other agent[j] should not be given Urls for that HostId
6.	precedence given to download Urls that other Urls depend on (so we can satisfy complete local autonomy)
7.	this takes no account of Localise (cf. dbo.p_ToLocalise)
*/
(	@Url			nvarchar(450)	= NULL		-- this is a LIKE value (e.g. 'http%://%DICK.com%') or NULL but makes EXPENSIVE query
,	@Extn			varchar(7)		= NULL
,	@Take			smallint		= 10
)
AS
BEGIN
SET NOCOUNT ON;				-- prevent extra result sets from interfering with SELECT statements

declare	@SPID		smallint		= @@SPID
	,	@PrefHostId	int				= NULL
	,	@clock		smalldatetime	= getdate()					-- deterministic timestamp for this batch
	,	@retrytime	smalldatetime	= getdate() - '00:20:00'	-- threshold for retry eligibility

-- 1.	search Agents table whether SPID is absent/present and INSERT/DELETE/UPDATE accordingly
SELECT	@PrefHostId = PrefHostId
from	dbo.Agents (nolock)
where	Spid = @SPID
if @PrefHostId is NULL
  begin
	INSERT INTO dbo.Agents
	(	Spid
	,	[Url]
	--,	FirstCall
	--,	LastCall
	)
	values
	(	@Spid
	,	@Url
	--,	getdate()
	--,	getdate()
	)
  end
 else
  begin
  -- declare @SPID smallint = @@SPID
	DELETE D
	from	dbo.Downloading		D
	join	dbo.WebPages		W	on	W.PageId	= D.PageId
	where	D.Spid			= @Spid
	 and	(	W.Download	= 0				-- no download required
			or	W.Download	= 3				-- completely downloaded
	--		or	W.Filespec	is not NULL		-- forces CI index scan (inefficient)
			)
	UPDATE dbo.Downloading set Retry	= Retry +1
	where	Spid		= @Spid
	 and	LastCall	=
			(	select	max(LastCall)
				from	dbo.Downloading (nolock)
				where	Spid	= @Spid
			)
  end

-- 2.	create temporary table to hold results, then use it to populate Downloading table and return WebPages entity
-- declare @Take smallint = 20, @SPID smallint = @@SPID, @PrefHostId int = 0
declare @results TABLE
(	PageId			int
,	HostId			int
,	[Url]			nvarchar(450)
,	DraftFilespec	nvarchar(260)
,	Filespec		nvarchar(260)	default NULL
,	Download		tinyint
,	Localise		tinyint
-- next 2 columns can be eliminated from @results since they are used for ORDER BY but nothing afterwards (except maybe for diags in step #7)
,	N				int
,	M				int
)

-- 3.	populate interim results table
INSERT @results (PageId, HostId, [Url], DraftFilespec, Filespec, Download, Localise, N, M)
	SELECT top (@Take)	W.PageId, W.HostId, W.[Url], DraftFilespec, Filespec, Download, Localise, N, M
	from	dbo.WebPages		W
	join
	(	SELECT	count(*) as N, HostId			-- N: count # items by sub-domain yet to download
		from	dbo.WebPages (nolock)
		where	Download	in (1, 2)			-- virgin download, re-download
		-- and	Filespec	is NULL
		group by HostId
	)							H			ON		H.HostId	= W.HostId
	left join
	(	SELECT	count(*) as ANO, HostId			-- another SPID active on this sub-domain ?
		from	dbo.Downloading	(nolock)	D2
		join	dbo.WebPages				W2	ON	W2.PageId	= D2.PageId
		where	Spid		!=	@SPID
		 and	D2.LastCall	>	@retrytime		-- still busy ?
		group by HostId
	)							A			ON		A.HostId	= W.HostId
	left join	dbo.Downloading	D (nolock)	ON		D.PageId	= W.PageId		-- eligible for new download
												and	D.LastCall	> @retrytime	--  or retry after timeout expired ?
	left join
	(	SELECT	count(*) as M, ParentId			-- M: count # pages are dependent on this page
		from	dbo.Depends (nolock)
		group by ParentId
	)							Z			ON		Z.ParentId	= W.PageId
	where	W.Download	in (1, 2)			-- to be downloaded [again] ?
	-- and	W.Filespec		is NULL			--  already downloaded
-- these 2 criteria are commented-out as too expensive (i.e. currently ignoring 2 input params)
	 --and	(	W.[Url]		like	@Url
		--	or	@Url		is NULL
		--	)
	 --and	(	W.DraftExtn = @Extn
		--	or	@Extn		is NULL
		--	)
	 and	D.PageId		is NULL
	 order by
			case when W.HostId = @PrefHostId then 0 else 1 end	-- agent should stay with same [preferred] Host
	,	N desc					-- # pages from this host waiting
	,	isnull(A.ANO, 0)		-- # agents also working on this Host
	,	isnull(M, 0) desc		-- # pages dependent on this page
	,	HostId					-- favour older Host
	,	W.PageId				-- favour older request

 -- 4.	record the batch returning to calling agent to process
MERGE INTO	dbo.Downloading as TGT
USING
(	SELECT	PageId, @Spid as Spid, @clock as Clock
	from	@results
)							as	SRC		ON	SRC.PageId = TGT.PageId
when matched then
	UPDATE set
		LastCall = Clock
	,	Spid	= SRC.Spid
when NOT matched then
	INSERT (PageId, Spid, FirstCall, LastCall)
		values (SRC.PageId, SRC.Spid, Clock, Clock)
;

-- 5.	determine majority subdomain and establish as agent's PrefHostId
SET @PrefHostId = isnull
(	(	SELECT top 1 HostId
		from	@results
		group by HostId
		order by count(*) desc, HostId
	)
	, 0											-- ensure don't carry forward from any previous batch
)
-- 6.	record timestamp of this call
UPDATE	dbo.Agents set
		LastCall	= @clock					-- record last sproc invocation
	,	PrefHostId	= @PrefHostId				--  even if no candidate rows found (that match input param criteria)
where	Spid	= @SPID

-- 7. return to UI for DEBUG
-- SELECT * from @results			-- DISABLE for PROD

-- 8.	return exact entity rows to client app
SELECT	PageId, [Url], DraftFilespec, Filespec, Download, Localise
from	@results

END