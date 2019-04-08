
/*
Author:		dbaker
-- Create date: 
EXAMPLES
	select 	dbo.fn_extension(null)
	select 	dbo.fn_extension('')
	select 	dbo.fn_extension('2226554')
	select 	dbo.fn_extension('2226554.xml')
	select 	dbo.fn_extension('2226554.abc.xml')
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
		return  substring(@filespec,@delim+1,999)
	return null
END