CREATE TABLE [dbo].[Downloading] (
    [PageId]    INT           NOT NULL,
    [Spid]      SMALLINT      NULL,
    [FirstCall] SMALLDATETIME CONSTRAINT [DF_Downloading_FirstCall] DEFAULT (getdate()) NOT NULL,
    [LastCall]  SMALLDATETIME CONSTRAINT [DF_Downloading_LastCall] DEFAULT (getdate()) NOT NULL,
    [Retry]     INT           CONSTRAINT [DF_Downloading_Attempt] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_Downloading] PRIMARY KEY CLUSTERED ([PageId] ASC),
    CONSTRAINT [FK_Downloading_Agents] FOREIGN KEY ([Spid]) REFERENCES [dbo].[Agents] ([Spid]) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT [FK_Downloading_WebPages] FOREIGN KEY ([PageId]) REFERENCES [dbo].[WebPages] ([PageId]) ON DELETE CASCADE ON UPDATE CASCADE
);






GO
CREATE NONCLUSTERED INDEX [IX_Downloading_SPID]
    ON [dbo].[Downloading]([Spid] ASC);

