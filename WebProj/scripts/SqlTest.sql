declare @url nvarchar(50)='www.pinvoke.net/default.aspx/shell32.SHGetFileInfo'
select dbo.UrlSplit(@url)
