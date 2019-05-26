CREATE TABLE [dbo].[Agents] (
    [Spid]       SMALLINT       NOT NULL,
    [PrefHostId] INT            NULL,
    [Url]        NVARCHAR (450) NULL,
    [LastCall]   SMALLDATETIME  CONSTRAINT [DF_Agents_LastCall] DEFAULT (getdate()) NOT NULL,
    CONSTRAINT [PK_Agents] PRIMARY KEY CLUSTERED ([Spid] ASC),
    CONSTRAINT [FK_Agents_Hosts] FOREIGN KEY ([PrefHostId]) REFERENCES [dbo].[Hosts] ([HostId])
);





