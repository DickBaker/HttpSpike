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

EXAMPLE
	select * from dbo.Hosts where HostName like '%.DICK.COM'
	select * from dbo.WebPages where Url like '%.DICK.COM%'

	delete dbo.Hosts where HostName like '%.DICK.COM'		-- should cascade delete WebPages
	delete dbo.WebPages where Url like '%.DICK.COM%'		--  so should be none
	INSERT INTO dbo.WebPages(Url) VALUES ('https://junk.DICK.COM/pages/index.html')

	select * from dbo.Hosts where HostName like '%.DICK.COM'
	select * from dbo.WebPages where Url like '%.DICK.COM%'

NOTES
1.	the HostId column is enforced by this trigger, so any supplied by client is effectively ignored
2.	thus C# clients should mark column as [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
*/
AS 
BEGIN
	SET NOCOUNT ON		-- prevent extra result sets from interfering with SELECT statements.
	
	IF (ROWCOUNT_BIG() = 0)				-- cf https://docs.microsoft.com/en-us/sql/t-sql/statements/create-trigger-transact-sql?view=sql-server-2017
		RETURN;

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
				HostId	= H.HostId
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
		join	dbo.Hosts		H	on	H.HostName	= dbo.UrlSplit(I.[Url])
		where	WP.HostId		!= H.HostId
		  or	WP.NeedDownload	is NULL
 

	END

END

GO
CREATE NONCLUSTERED INDEX [IX_HostId]
    ON [dbo].[WebPages]([HostId] ASC);


GO
