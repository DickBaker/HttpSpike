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
   ON  [dbo].[WebPages] 
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
	20180422 dbaker	changed to Download, Localise and persist *Extn

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
4.	because trigger does UPDATE WebPages (2 statements), the trigger will re-fire (sp_configure 'nested trigger', 1) but should quiesce rapidly (non-infinite!)
*/
AS 
BEGIN
	SET NOCOUNT ON		-- prevent extra result sets from interfering with SELECT statements.
	
	--IF (ROWCOUNT_BIG() = 0)				-- cf https://docs.microsoft.com/en-us/sql/t-sql/statements/create-trigger-transact-sql?view=sql-server-2017
	--	RETURN;

	IF		UPDATE([Url])				-- cf https://docs.microsoft.com/en-us/sql/t-sql/functions/update-trigger-functions-transact-sql?view=sql-server-2017
		or	UPDATE(HostId)				-- NB nested triggers server configuration option is OFF to avoid infinite loop
		or	UPDATE(Download)
	BEGIN

		INSERT INTO dbo.Hosts (HostName)
			select	DISTINCT I.HostName
			from
			(	SELECT	distinct dbo.UrlSplit([Url]) as HostName
				FROM	inserted	-- dbo.WebPages
			)	I
			left join	dbo.Hosts H		on	H.HostName	= I.HostName
			where	I.HostName	is not NULL
			 and	H.HostId	is NULL
			order by I.HostName

		UPDATE	WP set
				[Url]	=
					case
						when right(WP.[Url], 1)='/' or right(WP.[Url], 1) = '?'	-- any remove trailing / or ? (ideally both!)
							then	left(WP.[Url], len(WP.[Url]) -1)
						else		WP.[Url]
					end
			,	HostId	= H.HostId
			,	Download =					-- NULL=unknown, 0=no download, 1=to download, 2=to re-download, 3=downloaded
					case
						when WP.Download <=	0											then 0
						when WP.Download is NULL	and WP.DraftFilespec like '%.html'	then convert(tinyint, H.IsHtml)
						when WP.Download is NULL	and WP.DraftFilespec like '%.css'	then convert(tinyint, H.IsCss)
						when WP.Download is NULL	and WP.DraftFilespec like '%.js'	then convert(tinyint, H.IsJs)
						when WP.Download is NULL	and WP.DraftFilespec like '%.json'	then convert(tinyint, H.[IsJson])
						when WP.Download is NULL	and WP.DraftFilespec like '%.xml'	then convert(tinyint, H.IsXml)
						when WP.Download is NULL	and dbo.fn_extension(WP.DraftFilespec)
									in ('gif','ico','jpeg','jpg','png','svg')			then convert(tinyint, H.IsImage)
						when WP.Download is NULL	and WP.DraftFilespec is not NULL	then convert(tinyint, H.IsOther)	-- extn=null is legit
						when WP.Download is NULL	and WP.DraftFilespec is NULL		then 0		-- fs=null legit if FindLinks can't find fn, default to skip download
						when WP.Download >=	3		and WP.Filespec		is not NULL		then 3
						when WP.Download >	1		and WP.Filespec		is NULL			then 1
						else WP.Download		-- accept NN given
					end
			,	DraftExtn		= dbo.fn_extension(WP.DraftFilespec)		-- PERSISTED so deterministic
			,	FinalExtn		= dbo.fn_extension(WP.Filespec)				-- and eligible as index field
		from	inserted		I
		join	dbo.WebPages	WP	on	WP.PageId	= I.PageId	-- (PageId is immutable)
		join	dbo.Hosts		H	on	H.HostName	= dbo.UrlSplit(WP.[Url])
		where	WP.HostId		is NULL							-- always the case for C# app because EF ignorant of HostId
		  or	WP.Download		is NULL 
		  or	WP.HostId		!= H.HostId

	END

	-- commented-out since row with defaulting *Filespec can still be target of pre-existing values elsewhere
	-- IF	UPDATE(DraftFilespec)			-- cf https://docs.microsoft.com/en-us/sql/t-sql/functions/update-trigger-functions-transact-sql?view=sql-server-2017
	--	or	UPDATE(Filespec)
	BEGIN
		UPDATE	TGT set
			DraftFilespec	=	isnull(TGT.DraftFilespec, SRC.DraftFilespec)
		,	Filespec		=	isnull(TGT.Filespec, SRC.Filespec)
		from	inserted		I											-- frozen original as written by app
		join	dbo.WebPages	X		on		X.PageId	=	I.PageId	-- latest which may have shortened Url (PageId is immutable)
		join	dbo.WebPages	SRC		on		SRC.[Url]	in	(X.[Url], 'http://'+substring(X.[Url], 9, 999), 'https://'+substring(X.[Url], 8, 999))
		join	dbo.WebPages	TGT		on		TGT.[Url]	in	(X.[Url], 'http://'+substring(X.[Url], 9, 999), 'https://'+substring(X.[Url], 8, 999))
		where	I.[Url]					like	'http%'
		 and	(	(	SRC.DraftFilespec	is not NULL
					and	TGT.DraftFilespec	is NULL
					)
				 or	(	SRC.Filespec		is not NULL
					and	TGT.Filespec		is NULL
					)
				)
	END

	if	UPDATE(Localise)										-- 0=no localisation, 1=to localise, 2=localised
	BEGIN
		UPDATE dbo.WebPages set
			Download	=	1									-- NULL=unknown, 0=no download, 1=to download, 2=to re-download, 3=downloaded
		where	(	Download	= 0
				--or	Download	is NULL						-- should be unnecessary as above code should have eliminated all NULLs
				)
		--and	Filespec	is NULL
		 and	PageId		in
		 (	select distinct D.ParentId
			from	inserted	I								-- frozen original as written by app (PageId is immutable)
			join	dbo.Depends	D	on	D.ChildId	= I.PageId
			where	I.Localise	= 1
		)
	END

END

GO
CREATE NONCLUSTERED INDEX [IX_HostId]
    ON [dbo].[WebPages]([HostId] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_Download_Localise]
    ON [dbo].[WebPages]([Download] ASC, [Localise] ASC, [PageId] ASC, [FinalExtn] ASC);

