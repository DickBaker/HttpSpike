CREATE TABLE [dbo].TagToExtns (
    TagId   INT          NOT NULL,
    [AttribName]   VARCHAR (50) NOT NULL,
    [Attrib1Name]  VARCHAR (50) NULL,
    [Attrib1Value] VARCHAR (50) NULL,
    [Attrib2Name]  VARCHAR (50) NULL,
    [Attrib2Value] VARCHAR (50) NULL,
    [Extn]         VARCHAR (10) NOT NULL,
    CONSTRAINT [FK_TagToExtns_Tags] FOREIGN KEY (TagId) REFERENCES [dbo].Tags (TagId) ON DELETE CASCADE ON UPDATE CASCADE
);
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'n1=v1;n2=v2', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TagToExtns', @level2type = N'COLUMN', @level2name = N'AttribName';
GO
CREATE CLUSTERED INDEX [CI_TagToExtns]
    ON [dbo].TagToExtns(TagId ASC, [AttribName] ASC, [Extn] ASC);
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'n1=v1;n2=v2', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TagToExtns', @level2type = N'COLUMN', @level2name = N'Attrib2Name';
GO
EXECUTE sp_addextendedproperty @name = N'MS_Description', @value = N'n1=v1;n2=v2', @level0type = N'SCHEMA', @level0name = N'dbo', @level1type = N'TABLE', @level1name = N'TagToExtns', @level2type = N'COLUMN', @level2name = N'Attrib1Name';
