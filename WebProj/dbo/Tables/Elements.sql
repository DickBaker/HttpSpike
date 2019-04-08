CREATE TABLE [dbo].[Elements] (
    [ElementId] INT           IDENTITY (1, 1) NOT NULL,
    [TagId]     INT           NOT NULL,
    [PageId]    INT           NOT NULL,
    [OuterHtml] VARCHAR (500) NULL,
    [InnerHtml] NCHAR (450)   NULL,
    [Xpath]     VARCHAR (780) NULL,
    CONSTRAINT [PK_Elements] PRIMARY KEY NONCLUSTERED ([ElementId] ASC),
    CONSTRAINT [FK_Elements_Tags] FOREIGN KEY ([TagId]) REFERENCES [dbo].[Tags] ([TagId]) ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT [FK_Elements_WebPages] FOREIGN KEY ([PageId]) REFERENCES [dbo].[WebPages] ([PageId]) ON DELETE CASCADE ON UPDATE CASCADE
);






GO
CREATE CLUSTERED INDEX [CI_Elements]
    ON [dbo].[Elements]([PageId] ASC);

