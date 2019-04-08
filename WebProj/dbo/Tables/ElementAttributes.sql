CREATE TABLE [dbo].[ElementAttributes] (
    [AttributeId] INT          IDENTITY (1, 1) NOT NULL,
    [ElementId]   INT          NOT NULL,
    [AttribName]  VARCHAR (50) NOT NULL,
    [AttribValue] VARCHAR (50) NULL,
    CONSTRAINT [PK_ElementAttributes] PRIMARY KEY NONCLUSTERED ([AttributeId] ASC),
    CONSTRAINT [FK_ElementAttributes_Elements] FOREIGN KEY ([ElementId]) REFERENCES [dbo].[Elements] ([ElementId]) ON DELETE CASCADE ON UPDATE CASCADE
);




GO
CREATE UNIQUE CLUSTERED INDEX [CI_ElementAttributes]
    ON [dbo].[ElementAttributes]([ElementId] ASC, [AttribName] ASC, [AttribValue] ASC, [AttributeId] ASC);

