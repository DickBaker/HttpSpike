CREATE TABLE [dbo].[GreyPages] (
    [PageId]    INT           NOT NULL,
    [Spid]      SMALLINT      NOT NULL,
    [FirstCall] SMALLDATETIME CONSTRAINT [DF_GreyPages_FirstCall] DEFAULT (getdate()) NOT NULL,
    [LastCall]  SMALLDATETIME CONSTRAINT [DF_GreyPages_LastCall] DEFAULT (getdate()) NOT NULL,
    [Attempt]   INT           CONSTRAINT [DF_GreyPages_Attempt] DEFAULT ((1)) NOT NULL,
    CONSTRAINT [PK_GreyPages] PRIMARY KEY CLUSTERED ([PageId] ASC, [Spid] ASC),
    CONSTRAINT [FK_GreyPages_Agents] FOREIGN KEY ([Spid]) REFERENCES [dbo].[Agents] ([Spid]) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT [FK_GreyPages_WebPages] FOREIGN KEY ([PageId]) REFERENCES [dbo].[WebPages] ([PageId]) ON DELETE CASCADE ON UPDATE CASCADE
);

