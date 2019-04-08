CREATE TABLE [dbo].[Agents] (
    [Spid]       SMALLINT      NOT NULL,
    [Url]        NVARCHAR (50) NULL,
    [LastCall]   SMALLDATETIME CONSTRAINT [DF_Agents_LastCall] DEFAULT (getdate()) NOT NULL,
    [PrefHostId] INT           CONSTRAINT [DF_Agents_PrefHostId] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_Agents] PRIMARY KEY CLUSTERED ([Spid] ASC)
);

