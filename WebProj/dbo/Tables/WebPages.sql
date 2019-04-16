CREATE TABLE [dbo].[WebPages] (
    [PageId]        INT            IDENTITY (1, 1) NOT NULL,
    [HostId]        INT            NULL,
    [Url]           NVARCHAR (450) NOT NULL,
    [DraftFilespec] NVARCHAR (511) NULL,
    [Filespec]      NVARCHAR (511) NULL,
    [NeedDownload]  BIT            NULL,
    [DraftExtn]     AS             ([dbo].[fn_extension]([DraftFilespec])),
    [FinalExtn]     AS             ([dbo].[fn_extension]([Filespec])),
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

NOTES
1.	the HostId column is enforced by this trigger, so any supplied by client is effectively ignored
2.	thus [if present] C# clients should mark HostId column as [DatabaseGenerated(DatabaseGeneratedOption.Computed)]starts NULL
3.	C# app should standardise the Url field, but this sproc simply will remove any trailing "/" or "?" (but not both) just to support SSMS/etc
4.	if NeedDownload starts NULL it is populated from Hosts.IsXXX, but this is never changed by final UPDATE below
*/
AS 
BEGIN
	SET NOCOUNT ON		-- prevent extra result sets from interfering with SELECT statements.
	
	--IF (ROWCOUNT_BIG() = 0)				-- cf https://docs.microsoft.com/en-us/sql/t-sql/statements/create-trigger-transact-sql?view=sql-server-2017
	--	RETURN;

	IF		UPDATE([Url])				-- cf https://docs.microsoft.com/en-us/sql/t-sql/functions/update-trigger-functions-transact-sql?view=sql-server-2017
		or	UPDATE(HostId)				-- NB nested triggers server configuration option is OFF to avoid infinite loop
		or	UPDATE(NeedDownload)
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
						when right(WP.[Url],1)='/' or right(WP.[Url],1)='?'	-- any remove trailing / or ? (ideally both!)
							then	left(WP.[Url], len(WP.[Url]) -1)
						else		WP.[Url]
					end
			,	HostId	= H.HostId
			,	WP.NeedDownload =
					case
						when WP.NeedDownload is not NULL								then WP.NeedDownload
						when WP.NeedDownload is NULL and WP.DraftFilespec like '%.html' then H.IsHtml
						when WP.NeedDownload is NULL and WP.DraftFilespec like '%.css'	then H.IsCss
						when WP.NeedDownload is NULL and WP.DraftFilespec like '%.js'	then H.IsJs
						when WP.NeedDownload is NULL and WP.DraftFilespec like '%.json'	then H.IsJson
						when WP.NeedDownload is NULL and WP.DraftFilespec like '%.xml'	then H.IsXml
						else H.IsOther
					end
		from	inserted		I
		join	dbo.WebPages	WP	on	WP.PageId	= I.PageId
		join	dbo.Hosts		H	on	H.HostName	= dbo.UrlSplit(WP.[Url])
		where	WP.HostId		is NULL			-- always the case for C# app because EF ignorant of HostId
		  or	WP.HostId		!= H.HostId
		  or	WP.NeedDownload	is NULL 

	END

	--IF	UPDATE(DraftFilespec)			-- cf https://docs.microsoft.com/en-us/sql/t-sql/functions/update-trigger-functions-transact-sql?view=sql-server-2017
	--	or	UPDATE(Filespec)				-- NB nested triggers server configuration option is OFF to avoid infinite loop
	BEGIN
		UPDATE	TGT set
			DraftFilespec	=	isnull(TGT.DraftFilespec, SRC.DraftFilespec)
		,	Filespec		=	isnull(TGT.Filespec, SRC.Filespec)
		from	inserted		I											-- frozen original as written by app
		join	dbo.WebPages	X		on		X.PageId	=	I.PageId	-- latest which may have shortened Url
		join	dbo.WebPages	SRC		on		SRC.[Url]	in	(X.[Url], 'http://'+substring(X.[Url], 9, 999), 'https://'+substring(X.[Url], 8, 999))
		join	dbo.WebPages	TGT		on		TGT.[Url]	in	(X.[Url], 'http://'+substring(X.[Url], 9, 999), 'https://'+substring(X.[Url], 8, 999))
		where	I.[Url]					like	'http%'
		 and	(	SRC.DraftFilespec	is not NULL
				or	SRC.Filespec		is not NULL
				)
		 and	(	TGT.DraftFilespec	is NULL
				or	TGT.Filespec		is NULL
				)
	END

END

GO
CREATE NONCLUSTERED INDEX [IX_HostId]
    ON [dbo].[WebPages]([HostId] ASC);


GO
