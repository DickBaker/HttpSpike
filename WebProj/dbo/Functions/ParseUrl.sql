
/*
	select * from  dbo.ParseUrl('app.pluralsight.com')
	select * from  dbo.ParseUrl('https://app.pluralsight.com')
	select * from  dbo.ParseUrl('http://app.pluralsight.com:8080')
	select * from  dbo.ParseUrl('app.pluralsight.com/player')
	select * from  dbo.ParseUrl('https://app.pluralsight.com/player')
	select * from  dbo.ParseUrl('app.pluralsight.com/player?course=angular-2-getting-started-update&author=deborah-kurata&name=angular-2-getting-started-update-m10&clip=6&mode=live')
	select * from  dbo.ParseUrl('https://app.pluralsight.com:8000/player?course=angular-2-getting-started-update&author=deborah-kurata&name=angular-2-getting-started-update-m10&clip=6&mode=live')
*/
CREATE FUNCTION dbo.ParseUrl(@url varchar(255))
RETURNS @UrlParts TABLE 
(
    -- columns returned by the function
    Scheme		varchar(100)	NOT NULL,
    ServerName	varchar(255)	NOT NULL,
    PortNo		int				NOT NULL,
    Args		varchar(511)	NOT NULL
)
AS
BEGIN
	-- extract component fields
	declare	@Scheme		varchar(100)
		,	@ServerName	varchar(255)
		,	@PortNo		int				= 80
		,	@Args		varchar(511)
	declare @c_scheme	varchar(100)	= '://'
	declare @dlim1		int				= charindex(@c_scheme,@url)
	if @dlim1 > 0
		select @Scheme = left(@url,@dlim1-1), @url = SUBSTRING(@url,@dlim1 + LEN(@scheme) -2, 999)
	else
		set @Scheme = ''

	set		@dlim1 = charindex('/', @url+'/')
	select	@ServerName = LEFT(@url, @dlim1-1), @Args = SUBSTRING(@url, @dlim1,999)
	set		@dlim1 = CHARINDEX(':', @ServerName)
	if @dlim1 > 0
		select @PortNo= SUBSTRING(@ServerName, @dlim1+1, 999), @ServerName = LEFT(@ServerName, @dlim1-1)

	-- copy the required columns to the result of the function

   INSERT @UrlParts
   SELECT @Scheme, @ServerName, @PortNo, @Args
   RETURN
END
