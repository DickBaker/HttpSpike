CREATE TABLE [dbo].[Hosts] (
    [HostId]    INT            IDENTITY (1, 1) NOT NULL,
    [ParentId]  INT            NULL,
    [HostName]  NVARCHAR (255) NOT NULL,
    [IsHtml]    BIT            CONSTRAINT [DF_Host_IsHtml] DEFAULT ((0)) NOT NULL,
    [IsCss]     BIT            CONSTRAINT [DF_Host_IsCss] DEFAULT ((1)) NOT NULL,
    [IsJs]      BIT            CONSTRAINT [DF_Host_IsJs] DEFAULT ((1)) NOT NULL,
    [IsJson]    BIT            CONSTRAINT [DF_Host_IsJson] DEFAULT ((1)) NOT NULL,
    [IsXml]     BIT            CONSTRAINT [DF_Host_IsXml] DEFAULT ((1)) NOT NULL,
    [IsOther]   BIT            CONSTRAINT [DF_Host_IsOther] DEFAULT ((1)) NOT NULL,
    [IsImage]   BIT            CONSTRAINT [DF_Hosts_IsDownload] DEFAULT ((1)) NOT NULL,
    [Priority]  TINYINT        CONSTRAINT [DF_Hosts_Priority] DEFAULT ((100)) NOT NULL,
    [WaitCount] INT            NULL,
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
	20190526 dbaker	total rewrite from cursor to set-based, assuming 'nested triggers'=true (i.e. recursive)

EXAMPLE
	select * from dbo.Hosts where HostName like '%DICK.CO%' order by HostId
	select * from dbo.WebPages where Url like '%DICK.CO%'	-- (slooow!)
	delete dbo.Hosts where HostName like '%DICK.CO%'		-- should cascade delete WebPages
	delete dbo.WebPages where Url like '%DICK.CO%'			--  so should be none or only ftp://ftp.dick.com/sales

	INSERT INTO dbo.WebPages(Url) VALUES ('http://junk.DICK.COM/pages/index.html')	-- should produce HostNames junk.DICK.COM, DICK.COM
	INSERT INTO dbo.WebPages(Url) VALUES ('https://DICK.CO.UK/pages/index.html')	--  DICK.CO.UK with ParentId=NULL and NOT linked to .CO.UK
	begin tran
		select * from [dbo].[Hosts] where HostName like '%academia-photos.com'
		update [dbo].[Hosts] set ParentId = NULL where HostName='0.academia-photos.com'
		select * from [dbo].[Hosts] where HostName like '%academia-photos.com'
	rollback
	begin tran
		select * from [dbo].[Hosts] where HostName like '%wordcamp.org'
		update [dbo].[Hosts] set ParentId = NULL, IsHtml=1, IsOther=0 where HostName='2019-columbus.publishers.wordcamp.org'
		select * from [dbo].[Hosts] where HostName like '%wordcamp.org'
		update [dbo].[Hosts] set IsHtml=0, IsOther=0 where HostName='publishers.wordcamp.org'
		select * from [dbo].[Hosts] where HostName like '%wordcamp.org'
	rollback
	exec sp_configure 'show advanced options', 1
	reconfigure
	exec sp_configure 'disallow results from triggers'		-- advanced option
	exec sp_configure 'nested triggers'
	exec sp_configure 'server trigger recursion'
	exec sp_configure 'show advanced options', 0
	reconfigure

NOTES
1.	will fire [WebPages_trIU] that will fire [Hosts_trIU] via "nested triggers" server configuration option (exec sp_configure 'nested triggers')
2.	any new TLD must be created with ParentId=0, (IsHtml, IsCss, IsJs, IsJson, IsXml, IsOtherand ensure no Host rows point to it already
*/
AS 
BEGIN
	SET NOCOUNT ON		-- prevent extra result sets from interfering with SELECT statements.
	
--	select @@NESTLEVEL as N1, @@TRANCOUNT as T1	-- ** DEBUG **
declare @NewDomain table
(	DomName		nvarchar(450)	not NULL Primary Key
,	IsHtml		bit				not NULL
,	IsCss		bit				not NULL
,	IsJs		bit				not NULL
,	[IsJson]	bit				not NULL
,	IsXml		bit				not NULL
,	IsOther		bit				not NULL
,	IsImage		bit				not NULL
,	[Priority]	tinyint			not NULL
)
insert @NewDomain
	select	H2.domain, min(H2.IsHtml) as IsHtml, min(H2.IsCss)as IsCss, min(H2.IsJs) as IsJs, min(H2.[IsJson]) as [IsJson]
		,	min(H2.IsXml) as IsXml, min(H2.IsOther) as IsOther, min(H2.IsImage) as IsImage, avg(H2.[Priority]) as [Priority]
	from
	(	select	H1.*, CHARINDEX('.', HostName, dot1+1) as dot2, SUBSTRING(HostName, dot1+1, 9999) as domain
		from
		(	select	HostId, HostName
				,	Convert(smallint,IsHtml) as IsHtml,	Convert(smallint,IsCss) as IsCss,		Convert(smallint,IsJs) as IsJs,			Convert(smallint,[IsJson]) as [IsJson]
				,	Convert(smallint,IsXml) as IsXml,	Convert(smallint,IsOther) as IsOther,	Convert(smallint,IsImage) as IsImage,	[Priority], CHARINDEX('.', HostName) as dot1
			from	inserted	I
			--where	ParentId	is NULL
		)	H1
		where	H1.dot1	> 1
	) H2
	left join dbo.Hosts H3	on	H3.HostName	= H2.domain
	where	H2.dot2		> 2
	 and	H3.HostName is NULL
	group by H2.domain
	order by H2.domain

	if @@ROWCOUNT > 0
	  begin
		insert dbo.Hosts (HostName, IsHtml, IsCss, IsJs, [IsJson], IsXml, IsOther, IsImage, [Priority])
			select	DomName, IsHtml, IsCss, IsJs, [IsJson], IsXml, IsOther, IsImage, [Priority]
			from	@NewDomain
			--order by DomName

--	select @@NESTLEVEL as N2, @@TRANCOUNT as T2	-- ** DEBUG **
		update C set ParentId = P.HostId
		from	@NewDomain	X
		join	dbo.Hosts	P	on	P.HostName =	X.DomName
		join	inserted	I	on	I.HostName like	'%.' + X.DomName
		join	dbo.Hosts	C	on	C.HostId	=	I.HostId
		where	(	P.ParentId	is	NULL		-- parent is top-level (there is no grandparent)
				or	P.ParentId	!=	0			-- or some grandparent but ignore known-spurious TLD "parents"
				)
		-- and	C.ParentId	is	NULL
--	select @@NESTLEVEL as N3, @@TRANCOUNT as T3	-- ** DEBUG **
	  end

END

GO


