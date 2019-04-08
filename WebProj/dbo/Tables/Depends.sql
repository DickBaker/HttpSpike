CREATE TABLE [dbo].[Depends] (
    [ParentId] INT NOT NULL,
    [ChildId]  INT NOT NULL,
    CONSTRAINT [PK_Depends] PRIMARY KEY CLUSTERED ([ParentId] ASC, [ChildId] ASC),
    CONSTRAINT [FK_Depends_WebPages_Child] FOREIGN KEY ([ChildId]) REFERENCES [dbo].[WebPages] ([PageId]),
    CONSTRAINT [FK_Depends_WebPages_Parent] FOREIGN KEY ([ParentId]) REFERENCES [dbo].[WebPages] ([PageId]) ON DELETE CASCADE ON UPDATE CASCADE
);




GO
CREATE NONCLUSTERED INDEX [IX_Depends_ChildId]
    ON [dbo].[Depends]([ChildId] ASC);

