CREATE TABLE [dbo].[WebPages] (
    [PageId]        INT            IDENTITY (1, 1) NOT NULL,
    [HostId]        INT            NULL,
    [Url]           NVARCHAR (450) NOT NULL,
    [DraftFilespec] NVARCHAR (260) NULL,
    [Filespec]      NVARCHAR (260) NULL,
    [Download]      TINYINT        NULL,
    [Localise]      TINYINT        CONSTRAINT [DF_WebPages_NeedLocalise] DEFAULT ((0)) NOT NULL,
    [DraftExtn]     VARCHAR (7)    NULL,
    [FinalExtn]     VARCHAR (7)    NULL,
    CONSTRAINT [PK_WebPages] PRIMARY KEY NONCLUSTERED ([PageId] ASC),
    CONSTRAINT [FK_WebPages_Hosts] FOREIGN KEY ([HostId]) REFERENCES [dbo].[Hosts] ([HostId]) ON DELETE CASCADE ON UPDATE CASCADE
);









GO
CREATE UNIQUE CLUSTERED INDEX CI_WebPages
    ON dbo.WebPages(Url ASC);
GO

CREATE TRIGGER [dbo].[WebPages_trIU] 
	ON [dbo].[WebPages] 
	AFTER INSERT, UPDATE
/*
PURPOSE
	populate Host table as we encounter new page sources

HISTORY
	20190115 dbaker	create with TSQL TVF
	20190116 dbaker	recreated with CLR UDF to parse URL component fields
	20190126 dbaker	code WHERE in case UrlSplit UDF returns NULL
	20190127 dbaker	add examples to verify the UPDATE works ok
	20190307 dbaker	set NeedDownload from dbo.Hosts.IsXXX defaults
	20190308 dbaker	add DISTINCT and add last OR clause
	20190329 dbaker	add ROWCOUNT_BIG and IF UPDATE(c) code, and NOTES
	20190416 dbaker	remove ROWCOUNT_BIG and final IF UPDATE(c) code around SRC/TGT code to support BF/CF behaviour
	20190421 dbaker	on Localise=1 set NeedDownload for each independent resource
	20190422 dbaker	changed to Download, Localise and persist *Extn
	20190503 dbaker acquire entire TABLOCK on WebPages for duration of this sproc !
	20190504 dbaker remove (TABLOCKX, HOLDLOCK) on WebPages as the app now supports Polly retries for ADO and EF
	20190507 dbaker remove changes [removing trailing /or ?] from Url
	20190507 dbaker	ensure changing the Http Scheme doesn't exceed the 450 max (i.e. 442, 443 magic below)
	20190507 dbaker	isolate INSERT Hosts under separate IF UPDATE() block
	20190530 dbaker	invoke dbo.fn_extension in subquery, and test for "='xxx'" not "like '%.xxx'"
	20190606 dbaker	rewrite via @NewPages as desired state testable before exec to skip if unneeded (cf. infinite trigger recursion)
	20190607 dbaker	extend sq in "MERGE into dbo.Hosts" block to cater for new hosts but where all WebPage entries are Download < 3

EXAMPLE
	select * from dbo.Hosts where HostName like '%DICK.CO%' order by HostId
	select * from dbo.WebPages where Url like '%DICK.CO%' order by PageId	-- (slooow!)
	delete dbo.Hosts where HostName like '%DICK.CO%'		-- should cascade delete WebPages
	delete dbo.WebPages where Url like '%DICK.CO%'			--  so should be none or only ftp://ftp.dick.com/sales

	INSERT INTO dbo.WebPages(Url) VALUES ('http://junk.DICK.COM/pages/index.html')	-- should produce HostNames junk.DICK.COM, DICK.COM
	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.CO.UK/pages/index.html')	--  DICK.CO.UK with ParentId=NULL and NOT linked to .CO.UK
	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.CO.UK/?')					--  should strip trailing

	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.COM/A')					-- *filespec null as HTTPS
	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.COM/B')
	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.COM/C')

	INSERT INTO dbo.WebPages(Url,DraftFilespec) VALUES ('http://DICK.COM/A', 'A.html')	-- HTTP but should backfill HTTPS
	INSERT INTO dbo.WebPages(Url,Filespec) VALUES ('http://DICK.COM/B', 'c:\temp\BB.html')
	INSERT INTO dbo.WebPages(Url,DraftFilespec,Filespec) VALUES ('http://DICK.com/C', 'C.html', 'c:\temp\CC.html')

	INSERT INTO dbo.WebPages(Url,DraftFilespec) VALUES ('http://DICK.COM/D', 'D.html')	-- HTTP with *filespec
	INSERT INTO dbo.WebPages(Url,Filespec) VALUES ('http://DICK.COM/E', 'c:\temp\EE.html')
	INSERT INTO dbo.WebPages(Url,DraftFilespec,Filespec) VALUES ('http://DICK.com/F', 'F.html', 'c:\temp\FF.html')
	
	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.COM/D')					-- *filespec null but WebPages_trIU should populate
	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.COM/E')
	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.COM/F')

	UPDATE dbo.WebPages set Localise=1 where [Url]='http://www.ligonier.org/blog/state-theology-does-sin-deserve-damnation') -- PageId=23700 

NOTES
1.	the HostId column is enforced by this trigger, so any supplied by client is effectively ignored
2.	thus [if present] C# clients should mark HostId column as [DatabaseGenerated(DatabaseGeneratedOption.Computed)]starts NULL
3.	if Download starts NULL it is populated from Hosts.IsXXX, but this is never changed by final UPDATE below
4.	this WebPages_trIU trigger may fire Hosts_trIU (sp_configure 'nested trigger', 1) but should quiesce rapidly (non-infinite!)
*/
AS 
BEGIN
	SET NOCOUNT ON		-- prevent extra result sets from interfering with SELECT statements.
	
	--IF (ROWCOUNT_BIG() = 0)		-- cf https://docs.microsoft.com/en-us/sql/t-sql/statements/create-trigger-transact-sql?view=sql-server-2017
	--	RETURN;

	/*
		public enum DownloadEnum : byte
		{
			Ignore = 0,
			Redirected,													// ConsumeFrom should have ONE entry, Filespec should be NULL
			Downloaded,
			LoPriorityDownload,											// 3
			HiPriorityDownload = 63,									// valid range is 3 .. 63 for all WebPage rows
			Default = (LoPriorityDownload + HiPriorityDownload) / 2,	// 33 midpoint is default on INSERT
			BoostMin = Default + 1,										// 34 midpoint is default on INSERT
			BoostMax = (BoostMin + HiPriorityDownload) / 2,				// 48 so boost is range 34 .. 48 (15 automatic notches)
																		// so 49-63 only set manually after UI action (15 manual notches)
			LoReserved = 64,											// 64-255 reserved for future definition
			HiReserved = byte.MaxValue									// byte is unsigned 8-bit integer (ditto TSQL tinyint) range 0-255
		}
	*/

	declare @NewPages TABLE
	(
		PageId			int				NOT NULL PRIMARY KEY,
		HostName		nvarchar(255)	NOT NULL,		-- column not in WebPage definition
		Download		tinyint			NULL,
		Localise		tinyint			NOT NULL,
		DraftExtn		varchar(7)		NULL,
		FinalExtn		varchar(7)		NULL
	)
	insert @NewPages
		select	I.PageId
			,	I.HostName
			,	case		-- NULL=unknown (take Hosts.Is* default), else as above DownloadEnum
					when I.Filespec like '~%'							then	0	-- some fatal error encountered so set DownloadEnum.Inactive
					when I.Download is NULL	and DraftExtn	= 'html'	then	convert(tinyint, ISNULL(H.IsHtml, 0))	* 33
					when I.Download is NULL	and DraftExtn	= 'css'		then	convert(tinyint, ISNULL(H.IsCss, 1))	* 33
					when I.Download is NULL	and DraftExtn	= 'js'		then	convert(tinyint, ISNULL(H.IsJs, 1))		* 33
					when I.Download is NULL	and DraftExtn	= 'json'	then	convert(tinyint, ISNULL(H.[IsJson], 1))	* 33
					when I.Download is NULL	and DraftExtn	= 'xml'		then	convert(tinyint, ISNULL(H.IsXml, 1))	* 33
					when I.Download is NULL	and DraftExtn	in
						('bmp', 'gif','ico','jpeg','jpg','png','svg')	then	convert(tinyint, ISNULL(H.IsImage, 1))	* 33
					when I.Download is NULL								then	convert(tinyint, ISNULL(H.IsOther, 1))	* 29
					when I.Download >=	64	and I.Filespec	is not NULL	then	2	-- DownloadEnum.Downloaded
					else I.Download											-- accept NN as given
				end									as Download
			,	case
					when		I.FinalExtn	!=	'html'	then	0			-- skip if I.FinalExtn is NULL
				--	when		I.DraftExtn	!=	'html'	then	0			--  ditto if I.DraftExtn is NULL
					when		I.Download	<	2		then	0			-- Ignore || Redirected don't need localisation
					when		I.Download	>	2
							and I.Localise	=	2		then	1			-- if another d/l requested then will need to re-localise
					else										I.Localise	-- otherwise accept the client's value
				end									as Localise
			,	DraftExtn
			,	FinalExtn
		from
		(	select	PageId, Filespec, Download, Localise	-- , HostId, DraftFilespec, DraftExtn, FinalExtn	-- recompute *Extn and ignore any specified!
				,	dbo.UrlSplit([Url])				as HostName
				,	dbo.fn_extension(DraftFilespec)	as DraftExtn			-- invoke once here in sq to avoid expensive likes in 1st case stmt
				,	dbo.fn_extension(Filespec)		as FinalExtn			-- PERSISTED so deterministic (and could be used as index column)
			from	inserted
		)						I
		left join
				dbo.Hosts		H	on	H.HostName	= I.HostName

--select * from @NewPages order by HostName
--select count(*) as N, HostName from @NewPages group by HostName order by HostName	-- ** DEBUG **
--select * from deleted order by Url

	if	UPDATE([Url])				-- cf https://docs.microsoft.com/en-us/sql/t-sql/functions/update-trigger-functions-transact-sql?view=sql-server-2017
	 or	UPDATE(Download)
	  begin

		MERGE into dbo.Hosts	as TGT
		using
		(	select	TOP 100 PERCENT							-- needed to satisfy ORDERBY clause
					HostName, sum(Wdelta) as Wdelta
			from
			(
				select	distinct HostName, 0 as Wdelta
				from	@NewPages
			 union all
				select	HostName, count(*) as Wdelta
				from	@NewPages
				where	Download > 2
				group by HostName
			 union all
				select HostName, -count(*) as Wdelta
				from
					(	select	dbo.UrlSplit([Url]) as HostName
						from	deleted	
						where	Download > 2
					)	D
				group by HostName
			)	DELTA
			group by HostName
			order by HostName							-- needs "TOP 100 PERCENT" above
		)						as SRC	on	SRC.HostName	= TGT.HostName
		WHEN NOT MATCHED			THEN
			INSERT	(HostName, WaitCount)		-- N.B. will fire Hosts_trIU
				values (SRC.HostName, Wdelta)
		WHEN MATCHED
		 AND	SRC.Wdelta	!= 0	THEN
			UPDATE	set TGT.WaitCount	+= SRC.Wdelta	-- N.B. will fire Hosts_trIU but NO-OP as IF UPDATE(HostName) is false
		;
			--SELECT	Z.HostName
			--from	@NewPages	Z
			--left join
			--		dbo.Hosts	H	on	H.HostName	= Z.HostName
			--where	Z.HostName	is not NULL
			-- and	H.HostId	is NULL
			--order by Z.HostName
	  end

	if		UPDATE([Url])			-- cf https://docs.microsoft.com/en-us/sql/t-sql/functions/update-trigger-functions-transact-sql?view=sql-server-2017
		or	UPDATE(HostId)			-- NB C# app EF data model currently ignorant of HostId
		or	UPDATE(Download)
		or	UPDATE(DraftFilespec)
		or	UPDATE(Filespec)		-- ensure changes to *Extn don't re-fire trigger
	  begin
		if exists														-- avoid unecessary I/O and infinite recursion
		(	select	1
			from	dbo.WebPages	W
			join	@NewPages		Z	on	Z.PageId	= W.PageId
			join	dbo.Hosts		H	on	H.HostName	= Z.HostName
			where	(W.Download		!= Z.Download	or	W.Download	is NULL)
			 or		(W.DraftExtn	!= Z.DraftExtn	or	W.DraftExtn	is NULL)
			-- or	W.DraftFilespec	!= Z.DraftFilespec
			-- or	W.Filespec		!= Z.Filespec
			 or		(W.FinalExtn	!= Z.FinalExtn	or	W.FinalExtn	is NULL)
			 or		(W.HostId		!= H.HostId		or	W.HostId	is NULL)
			 or		W.Localise		!= Z.Localise
			-- or	W.[Url]			!= Z.[Url]
		)
			UPDATE	W	set
					W.Download		= Z.Download
				,	W.DraftExtn		= Z.DraftExtn
			--	,	W.DraftFilespec	= Z.DraftFilespec
			--	,	W.Filespec		= Z.Filespec
				,	W.FinalExtn		= Z.FinalExtn
				,	W.HostId		= H.HostId
				,	W.Localise		= Z.Localise
			--	,	W.[Url]			= Z.[Url]
			from	dbo.WebPages	W
			join	@NewPages		Z	on	Z.PageId	= W.PageId
			join	dbo.Hosts		H	on	H.HostName	= Z.HostName
	  end

	-- commented-out since row with defaulting *Filespec can still be target of pre-existing values elsewhere
	-- IF	UPDATE(DraftFilespec)			-- cf https://docs.microsoft.com/en-us/sql/t-sql/functions/update-trigger-functions-transact-sql?view=sql-server-2017
	--	or	UPDATE(Filespec)
	  begin
		UPDATE	TGT set
			DraftFilespec	=	isnull(TGT.DraftFilespec, SRC.DraftFilespec)
		,	Filespec		=	isnull(TGT.Filespec, SRC.Filespec)
		from	inserted		I											-- frozen original as written by app
		join	dbo.WebPages	X		on		X.PageId	=	I.PageId	-- latest which may have shortened Url (PageId is immutable)
		join	dbo.WebPages	SRC		on		SRC.[Url]	in	(X.[Url], 'http://'+substring(X.[Url], 9, 443), 'https://'+substring(X.[Url], 8, 442))
		join	dbo.WebPages	TGT		on		TGT.[Url]	in	(X.[Url], 'http://'+substring(X.[Url], 9, 443), 'https://'+substring(X.[Url], 8, 442))
		where	I.[Url]					like	'http%'
		 and	(	(	SRC.DraftFilespec	is not NULL
					and	TGT.DraftFilespec	is NULL
					)
				 or	(	SRC.Filespec		is not NULL
					and	TGT.Filespec		is NULL
					)
				)
	  end

	if	UPDATE(Localise)										-- 0=no localisation, 1=to localise, 2=localised
	  begin
		UPDATE dbo.WebPages set
			Download	=	48									-- DownloadEnum.BoostMax
		where	(	Download	= 0								-- DownloadEnum.Ignore
				or	Download between 3 and 47					-- lower-priority download already queued
				--or	Download	is NULL						-- should be unnecessary as above code should have eliminated all NULLs
				)
		 and	(	Filespec	is NULL							-- un-attempted or
				or	Filespec	not like '~%'					--  non-errored
				)
		 and	PageId		in
		 (	select distinct D.ParentId
			from	inserted	I								-- frozen original as written by app (PageId is immutable)
			join	dbo.Depends	D	on	D.ChildId	= I.PageId
			where	I.Localise	= 1								-- LocaliseEnum.ToLocalise
		)
	  end

END

GO
CREATE NONCLUSTERED INDEX [IX_HostId]
    ON [dbo].[WebPages]([HostId] ASC);


GO
CREATE NONCLUSTERED INDEX [WebPages_Download_HostId]
    ON [dbo].[WebPages]([Download] ASC)
    INCLUDE([HostId]);

