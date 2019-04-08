
CREATE FUNCTION dbo.ListUrlsToLoad
/*
PURPOSE
	query tables to see which pages are eligible for download

HISTORY
	20190127 dbaker	created

EXAMPLE
	select * from dbo.ListUrlsToLoad(1, 0, 0, 0, 0, 0)	-- HTML
	select * from dbo.ListUrlsToLoad(0, 0, 0, 0, 1, 0)	-- XML
	select * from dbo.ListUrlsToLoad(0, 0, 0, 0, 0, 1)	-- other
*/
(	@Html	bit = 0
,	@Css	bit	= 0
,	@Js		bit	= 0
,	@Json	bit	= 0
,	@Xml	bit	= 1
,	@Other	bit	= 0
)
RETURNS @retFindReports TABLE 
	(	HostName		nvarchar(255)	NOT NULL
	,	PageId			int				NOT NULL
	,	HostId			int				NOT NULL
	,	[Url]			nvarchar(450)	NOT NULL
	,	DraftFilespec	nvarchar(511)	NULL
	,	Filespec		nvarchar(511)	NULL
	)
AS
BEGIN
	insert @retFindReports
		select	H.HostName, WP.PageId, WP.HostId, WP.[Url], WP.DraftFilespec, WP.Filespec
		from	dbo.WebPages	WP
		join	dbo.Hosts		H	on	H.HostId	= WP.HostId
		where	(	@Html				= 1
				and	WP.DraftFilespec	like	'%.html'
				and	H.IsHtml			=		1
				)
		 or		(	@Css				=		1
				and	WP.DraftFilespec	like	'%.css'
				and	H.IsCss				=		1
				)
		 or		(	@Js					=		1
				and	WP.DraftFilespec	like	'%.js'
				and	H.IsJs				=		1
				)
		 or		(	@Json				=		1
				and	WP.DraftFilespec	like	'%.json'
				and	H.[IsJson]			=		1
				)
		 or		(	@Xml				=		1
				and	WP.DraftFilespec	like	'%.xml'
				and	H.IsXml				=		1
				)
		 or		(	@Other				=		1
				and	WP.DraftFilespec	like	'%.html'
				and	WP.DraftFilespec	like	'%.css'
				and	WP.DraftFilespec	like	'%.js'
				and	WP.DraftFilespec	like	'%.json'
				and	WP.DraftFilespec	like	'%.xml'
				and	H.IsOther			=		1
				)
		order by H.HostName
   return
END