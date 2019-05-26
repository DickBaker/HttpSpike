

CREATE PROCEDURE [dbo].[p_ActionWebPage] 
/*
PURPOSE
	process newly-uploaded batch of rows

HISTORY
	20190320 dbaker created
	20190320 dbaker remove #WebPagesStaging.NeedDownload
	20190413 dbaker remove @DraftFilespec, @Filespec, @NeedDownload parameters (responsibility of EF entity not the ADO caller) and dependent row update
	20190503 dbaker acquire entire TABLOCK on WebPages for duration of this sproc !
	20190504 dbaker remove (TABLOCKX, HOLDLOCK) on WebPages as the app now supports Polly retries for ADO and EF
	20190509 dbaker populate explicit Download for independent pages if the dependant page will need localisation
	20190526 dbaker fix explicit Download for independent pages if the dependant page will need localisation

NOTES
0	at compile time #WebPagesStaging table does MOT have to exist, so [1st call only] the sproc will need JIT compilation at run-time
1	.. so temporary table must have been created & populated by remote client app (table private to this SPID and dropped on disconnect)
2	every row's Url field must be non-blank, absolute url address
3	#WebPagesStaging table rows specify the independent rows (that may/not pre-exist)
4	dependent row MUST pre-exist (i.e. @PageId will be non-zero) and @Url passed as a double-check as both are immutable
5	independent rows may pre-exist unknown by caller, so need upsert on every column
6	this sproc returns no data

EXAMPLE
	select * from dbo.WebPages where [Url]='http://ligonier.org'
	exec dbo.p_ActionWebPage 23078, 'http://ligonier.org', 'draft1.html', 'C:\temp\webcache\gj3vdojg.bxl.html', 1
*/
(	@PageId			int					-- not IDENTITY(1,1) and not PK as independent rows value will be 0
,	@Url			nvarchar(450)
)
AS
BEGIN
	SET NOCOUNT ON;	-- prevent extra result sets from interfering with SELECT statements

-- 1.	validate first (dependent) staging row with actual pre-existing WebPages row in database
-- declare @PageId int=187723, @Url nvarchar(450) = N'https://wiki.logos.com/iOS_Reader_5.5.1'
if charindex('http', @Url) = 0 and charindex('://', @Url) = 0
	set @Url = 'http://' + @Url

declare @Localise tinyint =
(	select	Localise
	from	dbo.WebPages					-- WITH (TABLOCKX, HOLDLOCK)	-- exclusive lock on entire table to avoid dealocks [complex joins otherwise spid1 takes A, B and spid2 takes B,A]
	where	PageId	= @PageId				-- these 2 fields are immutable
	 and	[Url]	= @Url
)
if @@ROWCOUNT != 1
  begin
	raiserror('p_ActionWebPage: invalid dependent WebPage(PageId=%d, Url=%s)', 16, 1, @PageId, @Url)
  end

-- 2.	upsert independent rows as required (NB the dependent row already processed in #2)
  MERGE INTO dbo.WebPages				-- with (TABLOCKX, HOLDLOCK)
			as TGT
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
				,	Download		=
						case
							when @Localise > 0 and TGT.Download between 3 and 33 then	34				-- request [prioritised] download
							else														TGT.Download	-- keep as-is
						end
		WHEN NOT MATCHED THEN
			INSERT
				(	[Url]
				,	DraftFilespec
				,	Download
				)
			VALUES
				(	SRC.[Url]
				,	SRC.DraftFilespec
				,	case
						when @Localise > 0 then	34				-- request [prioritised] download
						else					NULL			-- let WebPages_trIU plug in the default by type [cf. IsHtml/etc]
					end
				)
	;
--	select @@ROWCOUNT as MRGCNT

-- 3.	INSERT any missing Depends rows (LJ simpler than MERGE)
	INSERT INTO dbo.Depends
		(	ParentId		-- independent row
		,	ChildId			-- dependent row
		)
	  SELECT	top 100 percent WP.PageId as ParentId, @PageId as ChildId
			from	#WebPagesStaging	STG
			join	dbo.WebPages			WP	on	WP.[Url]	= STG.[Url]
			left join dbo.Depends			DEP	on	DEP.ParentId	= WP.PageId and	DEP.ChildId = @PageId
			where	WP.[Url]	!= @Url		-- this is PK so will use PK_WebPagesStaging_123 constraint
			 and	DEP.ParentId is NULL	-- i.e. not exists (BTW there no non-key columns to UPDATE)
			order by  STG.[Url]				-- optimise PK_Depends on target Depends table
--	select @@ROWCOUNT as DEPCNT

END