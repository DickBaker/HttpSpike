
CREATE PROCEDURE [dbo].[p_ActionWebPage] 
/*
PURPOSE
	process newly-uploaded batch of rows

HISTORY
	20190320 dbaker created
	20190320 dbaker remove #WebPagesStaging.NeedDownload

NOTES
0	at compile time #WebPagesStaging table does MOT have to exist, so [1st call only] the sproc will need JIT compilation at run-time
1	.. so temporary table must have been created & populated by remote client app (table private to this SPID and dropped on disconnect)
2	every row's Url field must be non-blank, absolute url address
3	table's first row details the curent dependent WebPage; rows 2-n describe the independent rows (that may/not pre-exist)
4	dependent row MUST pre-exist (i.e. PageId will be non-zero), but may need to UPDATE to refresh DraftFilespec, Filespec, NeedDownload columns
5	independent rows may pre-exist unkown by caller (who will always set PageId=0 and ignored here), so need upsert on every column
6	this sproc returns no data

EXAMPLE
	select * from dbo.WebPages where [Url]='http://ligonier.org'
	exec dbo.p_ActionWebPage 23078, 'http://ligonier.org', 'unknown.html', 'C:\temp\webcache\gj3vdojg.bxl.html', 1
*/
(	@PageId			int					-- not IDENTITY(1,1) and not PK as independent rows value will be 0
,	@Url			nvarchar(450)
,	@DraftFilespec	nvarchar(511)
,	@Filespec		nvarchar(511)
,	@NeedDownload	bit				= NULL
)
AS
BEGIN
	SET NOCOUNT ON;	-- prevent extra result sets from interfering with SELECT statements

-- 1.	validate first (dependent) staging row with actual pre-existing WebPages row in database
if charindex('http', @Url) = 0 and charindex('://', @Url) = 0
	set @Url = 'http://' + @Url
declare @DraftFilespec0		nvarchar(511)
	,	@Filespec0			nvarchar(511)
	,	@NeedDownload0		bit
select	@DraftFilespec0	= DraftFilespec		-- existing values in db
	,	@Filespec0		= Filespec
	,	@NeedDownload0	= NeedDownload
from	dbo.WebPages
where	PageId	= @PageId					-- these 2 fields are immutable
 and	[Url]	= @Url
if @@ROWCOUNT != 1
  begin
	raiserror('p_ActionWebPage: invalid dependent WebPage(PageId=%d, Url=%s)', 16, 1, @PageId, @Url)
  end

-- 2.	update dependent row if existing values differ from input params
if		@DraftFilespec0	!= @DraftFilespec
	or	@Filespec0		!= @Filespec
	or	@NeedDownload0	!= @NeedDownload
  begin
	UPDATE dbo.WebPages set
			DraftFilespec	= @DraftFilespec
		,	Filespec		= isnull(@Filespec, Filespec)
		,	NeedDownload	= @NeedDownload
	where	PageId = @PageId
--	select @@ROWCOUNT as UpdCNT
  end

-- 3.	upsert independent rows as required (NB the dependent row already processed in #2)
  MERGE INTO dbo.WebPages as TGT
		using
		(	-- declare @Url nvarchar(450) = 'http://ligonier.org'
			SELECT	distinct top 100 percent [Url], DraftFilespec
			from	#WebPagesStaging
			where	[Url]	!= @Url		-- ensure no accidental link-to-self
			order by  [Url]				-- this is PK so will use PK_WebPagesStaging_123 constraint
		)	as SRC
		on (TGT.[Url] = SRC.[Url])
		WHEN MATCHED THEN
			UPDATE SET
					DraftFilespec	= isnull(SRC.DraftFilespec, TGT.DraftFilespec)
		WHEN NOT MATCHED THEN
			INSERT
				(	[Url]
				,	DraftFilespec
				)
			VALUES
				(	SRC.[Url]
				,	SRC.DraftFilespec
				)
	;
--	select @@ROWCOUNT as MRGCNT

-- 4.	INSERT any missing Depends rows (LJ simpler than MERGE)
/*		SELECT	WP.*
		,	(select count(*) from dbo.Depends where ParentId=WP.PageId)	AS Parents
		,	(select count(*) from dbo.Depends where ChildId=WP.PageId)	AS Children
		from	#WebPagesStaging	STG
		join	dbo.WebPages		WP	on WP.[Url]	= STG.[Url]
		order by  STG.[Url]					-- optimise PK_Depends on target Depends table
*/
	INSERT INTO dbo.Depends
		(	ParentId		-- independent row
		,	ChildId			-- dependent row
		)
	  SELECT	top 100 percent WP.PageId as ParentId, @PageId as ChildId
			from	#WebPagesStaging	STG
			join	dbo.WebPages			WP	on WP.[Url]	= STG.[Url]
			left join dbo.Depends			DEP	on	DEP.ParentId	= WP.PageId and	DEP.ChildId = @PageId
			where	WP.[Url]	!= @Url		-- this is PK so will use PK_WebPagesStaging_123 constraint
			 and	DEP.ParentId is NULL	-- i.e. not exists (BTW there no non-key columns to UPDATE)
			order by  STG.[Url]				-- optimise PK_Depends on target Depends table
--	select @@ROWCOUNT as DEPCNT

END