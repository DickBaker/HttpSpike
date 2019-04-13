﻿CREATE TABLE [dbo].Tags (
    TagId INT          IDENTITY (1, 1) NOT NULL,
    [Tag]        VARCHAR (50) NOT NULL,
    CONSTRAINT [PK_Tags] PRIMARY KEY CLUSTERED (TagId ASC)
);


GO
CREATE UNIQUE NONCLUSTERED INDEX CI_Tags
    ON [dbo].Tags([Tag] ASC);
