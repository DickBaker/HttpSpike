CREATE TABLE [dbo].[Editions] (
    [EditionId] INT           IDENTITY (1, 1) NOT NULL,
    [PageId]    INT           NOT NULL,
    [Dated]     SMALLDATETIME CONSTRAINT [DF_Editions_Dated] DEFAULT (getdate()) NOT NULL,
    [Path]      VARCHAR (500) NOT NULL,
    [Checksum]  INT           NULL,
    CONSTRAINT [PK_Editions] PRIMARY KEY CLUSTERED ([EditionId] ASC),
    CONSTRAINT [FK_Editions_WebPages] FOREIGN KEY ([PageId]) REFERENCES [dbo].[WebPages] ([PageId])
);

