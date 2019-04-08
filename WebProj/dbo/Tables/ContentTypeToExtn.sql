CREATE TABLE [dbo].[ContentTypeToExtns] (
    [Template] VARCHAR (100) NOT NULL,
    [Extn]     VARCHAR (10)  NOT NULL,
    [IsText]   BIT           CONSTRAINT [DF_ContentTypeToExtns_IsText] DEFAULT ((0)) NOT NULL,
    CONSTRAINT [PK_ContentTypeToExtns] PRIMARY KEY CLUSTERED ([Template] ASC)
);

