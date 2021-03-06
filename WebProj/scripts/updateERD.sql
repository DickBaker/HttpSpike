-- updateERD.sql
--USE [WebProd]		-- target db
GO

DELETE TGT
-- select TGT.*
FROM	dbo.sysdiagrams		TGT
join	Web.dbo.sysdiagrams	SRC
	on	TGT.principal_id	= SRC.principal_id
		and	TGT.[name]		= SRC.[name]
GO

INSERT INTO [dbo].[sysdiagrams]
           ([name]
           ,[principal_id]
           ,[version]
           ,[definition])
SELECT	[name]
	,	principal_id
--	,	diagram_id
	,	[version]
	,	[definition]
FROM	[Web].dbo.sysdiagrams
order by diagram_id
GO
