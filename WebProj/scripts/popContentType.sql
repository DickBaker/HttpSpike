USE [Web]
GO

DELETE FROM [dbo].[ContentType]
GO

INSERT INTO [dbo].[ContentType]
           ([Template]
           ,[Extn]
           ,[IsText])
	SELECT [Template]
		  ,[Extn]
		  ,[IsText]
	FROM	DELLTOSH.[MediaTypes].[dbo].[ContentType]
GO


