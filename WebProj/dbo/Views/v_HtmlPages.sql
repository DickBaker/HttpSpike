
/*
DROP INDEX IF EXISTS [IX_Download_Localise] ON [dbo].[WebPages]

CREATE NONCLUSTERED INDEX [IX_Download_Localise] ON [dbo].[WebPages]
(
	Download, Localise, PageId, FinalExtn
) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
GO
*/

create view [dbo].[v_HtmlPages]
/*
PURPOSE
	join WebPages with Depends tables to show percentage of independent resources have already been downloaded for an .html page

HISTORY
	20190421 dbaker	created

EXAMPLE
	select top 50 * from dbo.v_HtmlPages order by GotRefs desc,[Url]
	select top 50 *, 100.0*GotRefs/TotRefs as ZZZ from dbo.v_HtmlPages order by ZZZ desc, GotRefs desc, [Url]
	select top 50 *, 100.0*GotRefs/TotRefs as ZZZ from dbo.v_HtmlPages order by ZZZ desc, GotRefs desc, [Url]
	select count(*)	--top 50 PageId, HostId, Url, DraftFilespec, Filespec, Download, Localise, TotRefs, GotRefs, PC, 100.0*GotRefs/TotRefs as ZZZ
	from dbo.v_HtmlPages order by PC desc, GotRefs desc, [Url]

	UPDATE top 10 W set Localise	= 1
	-- select	W.PageId, W.HostId, W.Url, W.DraftFilespec, W.Filespec, W.Download, W.Localise
	from	dbo.WebPages	W
	where	Download	= 3		-- Filespec	is not NULL
	 and	PageId in
		(	select PageId
			--select top 10 *, 10.0*GotRefs/TotRefs
			from	dbo.v_HtmlPages	V
			--where	TotRefs		= GotRefs				-- don't use PC column as real is an approx datatype
			-- and	TotRefs		> 0
			order by PC desc, GotRefs desc, [Url]
		)
	 and	Download	= 0
	 and	Localise	= 0
	select	W.PageId, W.HostId, W.Url, W.DraftFilespec, W.Filespec, W.Download, W.Localise
	from	dbo.WebPages	W
	where	Filespec	is not NULL
	 and	PageId in
		(	select top 10 PageId
			from	dbo.v_HtmlPages	V
			where	TotRefs		= GotRefs				-- don't use PC column as real is an approx datatype
			order by PC desc, [Url]
		)
	 and	Download	= 0
	 and	Localise	= 0

	select	W.PageId, W.HostId, W.Url, W.DraftFilespec, W.Filespec, W.Download, W.Localise
	from	dbo.WebPages	W
	join
		(	select top 10 PageId
			from	dbo.v_HtmlPages
			where	TotRefs		= GotRefs				-- don't use PC column as real is an approx datatype
			order by PC desc, [Url]
		)	V	on	V.PageId	= W.PageId
	where	IsDownloaded2	= 1
	 and	Download	= 0
	 and	Localise	= 0
*/
as
	select	PageId, HostId, Url, DraftFilespec, Filespec, Download, Localise, GotRefs, TotRefs, case when TotRefs > 0 then 100.0 * GotRefs / TotRefs else 0 end as	PC
	from
	(	select	PageId, HostId, Url, DraftFilespec, Filespec, Download, Localise
			,	(	select	count(*)					-- TotRefs: count # external resources needed (downloaded or not)
					from	dbo.Depends (nolock)	D
					where	D.ChildId	= W.PageId
				)	as	TotRefs
			,	(	select	count(*)					-- GotRefs: count # external resources needed and downloaded
					from	dbo.Depends (nolock)	D
					join	dbo.WebPages (nolock)	P	ON	P.PageId	= D.ParentId
					where	P.Download	= 3			-- independent resources already downloaded
						and	D.ChildId	= W.PageId
				)	as	GotRefs
		from	dbo.WebPages (nolock)				W
		where	W.Download	= 3		-- fully downloaded
		--and	Filespec	is not NULL
		 and	W.FinalExtn	= 'html'
		--and	W.Localise	= 1
	) A
	where	TotRefs > 0				-- prevent divide-by-zero, but also NO-OP if no external links (so no localisation to do)
	-- order by PC desc, [Url]