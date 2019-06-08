
CREATE PROCEDURE [dbo].[p_ToLocalise] 
/*
PURPOSE
	provide batch of WebPage rows for *.html files that can be localised (most/all dependent files now already downloaded)

HISTORY
	20190417 dbaker created
	20190420 dbaker tweaked to try index
	20190422 dbaker change NeedDownload/NeedLocalise to Download/Localise tinyint indicators, and *Extn persistent columns for index

	20190426 dbaker	coerce Download, Localise to tinyint
EXAMPLES
	exec dbo.p_ToLocalise
	exec dbo.p_ToLocalise	'http%://%DICK.com%'
	exec dbo.p_ToLocalise	'http%://%DICK.com%', 'html', @Take = 25
	exec dbo.p_ToLocalise	@Take = 15
begin tran	-- watch index usage !
	exec dbo.p_ToLocalise	@Take = 2500	--, @AllYN = 1		-- dependent must have 100% independents already downloaded
rollback

select *
-- update W set Localise=0
from WebPages W where PageId in (47686, 60107)
where  Download=3 and Localise>0
 and Filespec is not NULL and Filespec not like '~%'

SELECT * FROM [dbo].Depends D join WebPages W on W.PageId=D.ParentId
where D.ChildId in (28096, 47686)
and W.[Url] like '%Keyword%'
order by D.ChildId, W.[Url]

SELECT W.*, D.*
FROM [dbo].Depends D
join WebPages W on W.PageId=D.ChildId or W.PageId=55752
where D.ParentId = 55752
order by W.[Url], D.ChildId

update W set Download=2
-- select *
from WebPages W
where Download=3
 and 
order by [Url]

select count(*) as N, Download
from WebPages W
group by Download
order by Download

select * from WebPages where [Url] like '%Keyword%' order by [Url]

NOTES
1.	Agents table is indexed by the SPID of the calling client (assumed to be an agent)
2.	dbo.Downloading table keeps short-term memory of the batch (of 10) requests given to the agent (previous call)
3.	as pages are given to calling agent, the Localise bit is cleared
4.	precedence given to download files Urls that have most dependents already downloaded (i.e. Filespec non-NULL)
*/
(	@Url			nvarchar(450)	= NULL		-- this is a LIKE value (e.g. 'http%://%DICK.com%') or NULL but makes EXPENSIVE query
,	@Take			smallint		= 20		-- batch size
,	@AllYN			bit				= 0			-- 1 = must have all dependent files already downloaded to be eligible
)
AS
BEGIN
SET NOCOUNT ON;				-- prevent extra result sets from interfering with SELECT statements

declare	@SPID		smallint		= @@SPID
	,	@clock		smalldatetime	= getdate()					-- deterministic timestamp for this batch

-- 1.	search Agents table whether SPID is absent/present and INSERT/DELETE/UPDATE accordingly
if not exists(SELECT 1 from dbo.Agents (nolock) where Spid = @SPID)
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
 else					-- should be unnecessary as final call to p_ToDownload should have hosed, but safer just in case
  begin
  -- declare @SPID smallint = @@SPID
	DELETE D
	from	dbo.Downloading		D
	join	dbo.WebPages		W	on	W.PageId	= D.PageId
	where	D.Spid			= @Spid
	 and	(	W.Download	< 3				-- completely downloaded(2), redirect(1) or Ignore(0)
	--		or	W.Filespec	is not NULL		-- forces CI index scan (inefficient)
			)
  end

-- 2.	create temporary table to hold results, then use it to populate Downloading table and return WebPages entity
-- declare @Take smallint = 20, @SPID smallint = @@SPID
declare @results TABLE
(	PageId			int
,	[Url]			nvarchar(450)
,	DraftFilespec	nvarchar(260)
,	Filespec		nvarchar(260)
,	TotRefs			int
,	GotRefs			int
,	PC				real
--,	HostId			int
--,	Download		tinyint			default 3
--,	Localise		tinyint			default 1
)

-- 3.	populate interim results table
INSERT @results (PageId, [Url], DraftFilespec, Filespec, TotRefs, GotRefs, PC)
-- declare @Take int = 20, @AllYN bit = 0
	select	PageId, [Url], DraftFilespec, Filespec, TotRefs, GotRefs
	--	,	case when TotRefs > 0 then 100.0 * GotRefs / TotRefs else 0.0 end as PC
		,	100.0 * GotRefs / TotRefs as PC				-- cf. WHERE clause below avoids div-by-zero
	from
	(	select	top (@Take)	PageId, [Url], DraftFilespec, Filespec	--, HostId, Download, Localise
		,	(select	count(*)						-- count # external resources needed (downloaded or not)
				from	dbo.Depends (nolock)	D
				where	D.ChildId	= W.PageId
			)	as	TotRefs
		,	(	select	count(*)					-- GotRefs: count # external resources needed and downloaded
				from	dbo.Depends (nolock)	D
				join	dbo.WebPages (nolock)	P	ON	P.PageId	= D.ParentId
				where	P.Download	= 2				-- independent resource already downloaded
				--and	P.Filespec	is NOT NULL
				 and	D.ChildId	= W.PageId
			)	as	GotRefs
		from	dbo.WebPages	W
		where	Filespec	is NOT NULL
			and	FinalExtn	= 'html'
			and	Download	= 2				-- this dependent Html page already downloaded & links extracted
			and	Localise	= 1				--  and localisation requested but not yet completed
	)	as	A
	where	TotRefs			> 0				-- if using no external independent resources then NO-OP (and avoid div-by-zero)
	and		(	TotRefs		= GotRefs		-- don't need "isnull(GotRefs, 0)" here
			or	@AllYN	= 0
			)
	 order by
			PC desc							-- % downloaded (may be zero)
		,	GotRefs desc					-- # downloaded pages consumed by this page (N.B. if zero then nothing to localise)
		,	PageId							-- favour older request

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
	,	Retry	= -999			-- should not occur !
when NOT matched then
	INSERT (PageId, Spid, FirstCall, LastCall, Retry)
		values (SRC.PageId, SRC.Spid, Clock, Clock, -1)
;

-- 5.	record timestamp of this call
UPDATE	dbo.Agents set
		LastCall	= @clock		-- record last sproc invocation
	,	PrefHostId	= NULL			--  not doing downloads now (cf. FK_Agents_Hosts)
where	Spid	= @SPID

-- 6. return to UI for DEBUG
-- SELECT * from @results			-- DISABLE for PROD

-- 7.	return exact entity rows to client app (N.B. client ignorant of HostId)
SELECT	PageId, [Url], DraftFilespec, Filespec, convert(tinyint,2) as Download, convert(tinyint,1) as Localise	-- need to cast as int8 otherwise will be int32
from	@results

END