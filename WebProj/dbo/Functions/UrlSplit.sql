CREATE FUNCTION [dbo].[UrlSplit]
(@url NVARCHAR (MAX) NULL)
RETURNS NVARCHAR (MAX)
AS
 EXTERNAL NAME [ClrLib].[NetUtils.UserDefinedFunctions].[UrlSplit]

