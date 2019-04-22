CREATE TABLE [dbo].[Hosts] (
    [HostId]   INT            IDENTITY (1, 1) NOT NULL,
    [ParentId] INT            NULL,
    [HostName] NVARCHAR (255) NOT NULL,
    [IsHtml]   BIT            CONSTRAINT [DF_Host_IsHtml] DEFAULT ((0)) NOT NULL,
    [IsCss]    BIT            CONSTRAINT [DF_Host_IsCss] DEFAULT ((1)) NOT NULL,
    [IsJs]     BIT            CONSTRAINT [DF_Host_IsJs] DEFAULT ((1)) NOT NULL,
    [IsJson]   BIT            CONSTRAINT [DF_Host_IsJson] DEFAULT ((1)) NOT NULL,
    [IsXml]    BIT            CONSTRAINT [DF_Host_IsXml] DEFAULT ((1)) NOT NULL,
    [IsOther]  BIT            CONSTRAINT [DF_Host_IsOther] DEFAULT ((1)) NOT NULL,
    [IsImage]  BIT            CONSTRAINT [DF_Hosts_IsDownload] DEFAULT ((1)) NOT NULL,
    CONSTRAINT [PK_Hosts] PRIMARY KEY NONCLUSTERED ([HostId] ASC),
    CONSTRAINT [FK_Hosts_ParentHost] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[Hosts] ([HostId])
);






GO
CREATE UNIQUE CLUSTERED INDEX [CI_Hosts]
    ON [dbo].[Hosts]([HostName] ASC);

GO

CREATE TRIGGER [dbo].[Hosts_trIU] 
   ON  [dbo].[Hosts] 
   AFTER INSERT, UPDATE
/*
PURPOSE
	populate Host table as we encounter new page sources, progressively removing subdomains up to TLD

HISTORY
	20190416 dbaker	created (after populating HostId=0 and ParentId=0 TLD records (ac.uk co.nz co.uk com.br com.mx org.au)

EXAMPLE
	select * from dbo.Hosts where HostName like '%DICK.CO%' order by HostId
	select * from dbo.WebPages where Url like '%DICK.CO%'	-- (slooow!)
	delete dbo.Hosts where HostName like '%DICK.CO%'		-- should cascade delete WebPages
	delete dbo.WebPages where Url like '%DICK.CO%'			--  so should be none or only ftp://ftp.dick.com/sales

	INSERT INTO dbo.WebPages(Url) VALUES ('http://junk.DICK.COM/pages/index.html')	-- should produce HostNames junk.DICK.COM, DICK.COM
	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.CO.UK/pages/index.html')	--  DICK.CO.UK with ParentId=NULL and NOT linked to .CO.UK

NOTES
1.	will fire [WebPages_trIU] that will fire [Hosts_trIU] via "nested triggers" server configuration option (exec sp_configure 'nested triggers')
2.	any new TLD must be created with ParentId=0, (IsHtml, IsCss, IsJs, IsJson, IsXml, IsOtherand ensure no Host rows point to it already
*/
AS 
BEGIN
	SET NOCOUNT ON		-- prevent extra result sets from interfering with SELECT statements.
	
	declare domcur cursor FORWARD_ONLY for
	select	HostId, ParentId, HostName
	from	inserted
	where	ParentId is NULL
	 and	CHARINDEX('.', HostName) > 0
	 --and	CHARINDEX('.', HostName,CHARINDEX('.', HostName)+1) > 0
	order by HostName
	-- for update of ParentId (done in loop and not "where current of")
	open domcur
	declare @HostId int, @ParentId int, @ParentId2 int, @delim1 int, @HostName nvarchar(255), @ParentName nvarchar(255)
	fetch next from domcur into @HostId, @ParentId, @HostName
	while @@FETCH_STATUS=0
	  begin
--		PRINT @HostName		-- DEBUG
		set @delim1 = CHARINDEX('.', @HostName)
		if @delim1 = 0 or @delim1 = len(@HostName) continue		-- extreme (illegal!) case of leading/trailing "."
		while	@ParentId								is NULL
		 and	CHARINDEX('.', @HostName, @delim1+1)	>	0	-- ensure we are looking at (subN.}sub1.dom.tld syntax
		  begin
			set @ParentName = SUBSTRING(@HostName, @delim1+1,999)	-- e.g. sub1.dom.tld
			select	@ParentId	= HostId
				,	@ParentId2	= ParentId
			from	dbo.Hosts
			where	HostName	= @ParentName
			if @@ROWCOUNT=0
			  begin
				INSERT INTO dbo.Hosts
					(	HostName
					,	IsHtml
					,	IsCss
					,	IsJs
					,	[IsJson]
					,	IsXml
					,	IsOther
					)
					SELECT	@ParentName								-- e.g. sub1.dom.tld
						,	IsHtml									--  but clone other settings
						,	IsCss
						,	IsJs
						,	[IsJson]
						,	IsXml
						,	IsOther
					FROM	dbo.Hosts
					where	HostId	= @HostId
					set @ParentId = SCOPE_IDENTITY()
			  end
			 else
				if @ParentId2 = 0 continue							-- reached a TLD (e.g. "co.uk"), so quit current (do not update ParentId)

			update dbo.Hosts set ParentId	= @ParentId
			where	HostId	= @HostId

			select	@HostId		= @ParentId							-- advance to more senior host (e.g. dom.tld)
				,	@ParentId	= @ParentId2						-- N.B. doesn't matter if pre-existed so will appear in domcur cursor later
				,	@HostName	= @ParentName
				,	@delim1		= CHARINDEX('.', @ParentName)		-- and repeat the WHILE processing for "parent" hierarchically

		  end

		fetch next from domcur into @HostId, @ParentId, @HostName
	  end
	close domcur
	deallocate domcur

END

GO
DISABLE TRIGGER [dbo].[Hosts_trIU]
    ON [dbo].[Hosts];

