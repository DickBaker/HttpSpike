
/*
	select top 20 * from [dbo].[WebPages] order by len(Url) desc, Url
	https://i-msdn.sec.s-msft.com/Combined.css?resources=0:Teaser,0:Search,0:LocaleSelector,1:TabStrip,0:Footer,0:RatingsOnly,0:NetReflector,1:NetReflector,0:CorporatePromoSpot,0:Default,1:Default,2:Default,3:epxheader.4,3:sprite,3:epxfooter.4;/Areas/Sto/Content/Theming:0,/Areas/Sto/Content/Theming/msdn:1,/Areas/Sto/Content/Theming/msdn/vstudio2:2,/Areas/Epx/Themes/VStudio/Content:3&amp;amp;v=36A1E28309306427348A8B450146261B
*/

/*
PURPOSE
	extract the extension (after final ".") from full filesppec

HISTORY
	20190312 dbaker	created
	20190312 dbaker	truncate result to avoid 8152 "String or binary data would be truncated. The statement has been terminated"

EXAMPLES
	select 	dbo.fn_extension(null)
	select 	dbo.fn_extension('')
	select 	dbo.fn_extension('2226554')
	select 	dbo.fn_extension('2226554.xml')
	select 	dbo.fn_extension('2226554.abc.xml')
	select 	dbo.fn_extension('abc.fartoolong')
	select 	dbo.fn_extension('xyz.abc.fartoolong')
	select 	*, dbo.fn_extension(DraftFilespec) as DraftExtn, dbo.fn_extension(Filespec) as Extn
	from [dbo].[WebPages]
	where DraftFilespec is not NULL or Filespec is not NULL
	order by Url
	select 	count(*) as N, dbo.fn_extension(DraftFilespec) as DraftExtn, dbo.fn_extension(Filespec) as Extn
	from [dbo].[WebPages]
	where DraftFilespec is not NULL or Filespec is not NULL
	group by dbo.fn_extension(DraftFilespec), dbo.fn_extension(Filespec)
	order by N desc, DraftExtn, Extn
	select 	count(*) as N, dbo.fn_extension(DraftFilespec) as DraftExtn
	from [dbo].[WebPages]
	where DraftFilespec is not NULL or Filespec is not NULL
	group by dbo.fn_extension(DraftFilespec)
	order by N desc, DraftExtn
	select 	count(*) as N, dbo.fn_extension(Filespec) as Extn
	from [dbo].[WebPages]
	where Filespec is not NULL
	group by dbo.fn_extension(Filespec)
	order by N desc, Extn
*/
CREATE FUNCTION dbo.fn_extension 
(
	-- Add the parameters for the function here
	@filespec nvarchar(511)
)
RETURNS nvarchar(99)
AS
BEGIN
	declare @delim int = -1
		,	@delim2 int = charindex('.', isnull(@filespec,''))
	while @delim2 > 0
	  begin
		select	@delim=@delim2, @delim2= charindex('.', @filespec, @delim2+1)
	  end
	if @delim > 0
		return  substring(@filespec,@delim+1,7)			-- max length of WebPages.DraftExtn and FinalExtn
	return null
END