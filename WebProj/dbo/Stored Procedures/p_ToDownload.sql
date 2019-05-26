

CREATE PROCEDURE [dbo].[p_ToDownload] 
/*
PURPOSE
	process newly-uploaded batch of rows

HISTORY
	20190320 dbaker created
	20190417 dbaker	ignore Filespec when considering downloadability (may want re-download)
	20190419 dbaker	remove HostId and add NeedLocalise column from resultset
	20190423 dbaker	recode NeedDownload, NeedLocalise bit to Download, Localise byte to enhance valid states
	20190508 dbaker	update ORDER BY priorities
	20190509 dbaker	correct DownloadEnum.Downloaded (value 4) and DownloadEnum.Redirected (value 3). anticipate Download as priority (>2)
	20190513 dbaker	adapt to LoPriorityDownload .. HiPriorityDownload

EXAMPLES
	exec dbo.p_ToDownload
	exec dbo.p_ToDownload	'http%://%DICK.com%'
	exec dbo.p_ToDownload	'http%://%DICK.com%', 'html', @Take = 20
	exec dbo.p_ToDownload	@Extn = 'json', @Take = 15
	begin tran
		exec dbo.p_ToDownload	@Take = 15
	rollback

NOTES
1.	Agents table is indexed by the SPID of the calling client (assumed to be an agent)
2.	dbo.Downloading table keeps short-term memory of the batch (of 10) requests given to the agent (previous call)
3.	if any downloaded failed this last interval, entries are Retries++ and ineligible as download candidates for 20 minutes
4.	once agent[i] have been given a batch of Urls the majority HostId is remembered and takes precedence next call (encourages sticky cookies etc)
5.	then other agent[j] should not be given Urls for that HostId
6.	precedence given to download Urls that other Urls depend on (so we can satisfy complete local autonomy)
7.	this takes no account of Localise (cf. dbo.p_ToLocalise)
*/
(	@Take			smallint		= 10		-- batch-size (tradeoff between #agents and avg time to process batch)
,	@Url			nvarchar(450)	= NULL		-- this is a LIKE value (e.g. 'http%://%DICK.com%') or NULL but makes EXPENSIVE query
,	@Extn			varchar(7)		= NULL
)
AS
BEGIN
SET NOCOUNT ON;				-- prevent extra result sets from interfering with SELECT statements

declare	@PrefHostId	int				= NULL
	,	@clock		smalldatetime	= getdate()					-- deterministic timestamp for this batch
	,	@retrytime	smalldatetime	= getdate() - '00:20:00'	-- threshold for retry eligibility

-- 1.	search Agents table whether SPID is absent/present and INSERT/DELETE/UPDATE accordingly
SELECT	@PrefHostId = PrefHostId
from	dbo.Agents (nolock)
where	Spid = @@SPID
if @@ROWCOUNT = 0
 begin
	INSERT INTO dbo.Agents
	(	Spid
	,	[Url]
	--,	LastCall
	--,	PrefHostId
	)
	values
	(	@@SPID
	,	@Url
	--,	getdate()
	--,	NULL
	)
 end
 else
 begin
	DELETE D
	from	dbo.Downloading		D
	join	dbo.WebPages		W	on	W.PageId	= D.PageId
	where	D.Spid	= @@SPID
	 and	W.Download	between 0 and 2		-- no download required, redirected or completely downloaded
	UPDATE dbo.Downloading set
			Retry	= Retry +1
		,	Spid	= NULL						-- no longer bound to this agent (another agent could attempt download after retry interval elapses)
	where	Spid	= @@SPID
 end

-- 2.	create temporary table to hold results, then use it to populate Downloading table and return WebPages entity
-- declare @Take smallint = 20, @PrefHostId int = 0, @PrefHostId int=-1
declare @results TABLE
(	PageId			int
,	HostId			int
,	[Url]			nvarchar(450)
,	DraftFilespec	nvarchar(260)
,	Filespec		nvarchar(260)	default NULL
,	Download		tinyint
,	Localise		tinyint
)

-- 3.	populate interim results table
INSERT @results (PageId, HostId, [Url], DraftFilespec, Filespec, Download, Localise)
-- declare @Take smallint=10, @retrytime smalldatetime=getdate()-'00:20:00', @PrefHostId int=-1
	SELECT top (@Take)	W.PageId, W.HostId, W.[Url], DraftFilespec, Filespec, Download, Localise
	from
	(	select top 200								-- should pick good enough universe (e.g. to get best 30 by outer orderby criteria)
				PageId, HostId, [Url], DraftFilespec, Filespec, Download, Localise
		from	dbo.WebPages
		where	Download		between 3 and 63	-- LoPriorityDownload=3 .. HiPriorityDownload=63 inclusive
		-- and	Filespec		is NULL				-- already downloaded (or broken if ~...)
		order by Download desc
	)		W
	join	dbo.Hosts			H	on H.HostId = W.HostId
	left join
	(	-- declare @retrytime smalldatetime=getdate()-'00:20:00'
		SELECT	count(*) as ANO, HostId			-- another SPID active on this sub-domain ?
		from	dbo.Downloading	(nolock)	D2
		join	dbo.WebPages				W2	ON	W2.PageId	= D2.PageId
		where	Spid		!=	@@SPID
		 and	D2.LastCall	>	@retrytime		-- still busy ?
		group by HostId
	)							A			ON		A.HostId	= W.HostId
	left join	dbo.Downloading	D (nolock)	ON		D.PageId	= W.PageId		-- eligible for new download
												and	D.LastCall	> @retrytime	-- or retry after timeout expired ?
	where	W.Download		between 3 and 63	-- LoPriorityDownload=3 .. HiPriorityDownload=63 inclusive, then manual imperatives
	-- and	W.Filespec		is NULL				-- already downloaded (or broken if ~...)
	 and	D.PageId		is NULL
	-- these 2 criteria are commented-out as too expensive (i.e. currently ignoring 2 input params)
	 --and	(	W.[Url]		like	@Url
		--	or	@Url		is NULL
		--	)
	 --and	(	W.DraftExtn = @Extn
		--	or	@Extn		is NULL
		--	)
	 order by
		isnull(A.ANO, 0)						-- # other agents also working on this Host (hopefully none)
	,	H.Priority desc							-- # items from this host yet to download
	,	W.Download desc							-- prioritise download (based on # dependent pages)
	,	case when W.HostId = @PrefHostId then 0 else 1 end	-- agent should stay with same [preferred] Host
	,	W.HostId									-- favour older Host
	,	W.PageId								-- favour older request

 -- 4.	record the batch returning to calling agent to process
MERGE INTO	dbo.Downloading as TGT
USING
(	SELECT	PageId, @@SPID as Spid, @clock as Clock
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
SET @PrefHostId =
	(	SELECT top 1 HostId
		from	@results
		group by HostId
		order by count(*) desc, HostId
	)								-- ensure don't carry forward from any previous batch
-- 6.	record timestamp of this call
UPDATE	dbo.Agents set
		LastCall	= @clock					-- record last sproc invocation
	,	PrefHostId	= @PrefHostId				-- even if no candidate rows found (that match input param criteria)
	,	[Url]		= @Url						-- this input speccurrently ignored
where	Spid	= @@SPID

-- 7. return exact entity rows to client app
SELECT	PageId, [Url], DraftFilespec, Filespec, Download, Localise
from	@results

END