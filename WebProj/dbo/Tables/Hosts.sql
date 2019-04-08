CREATE TABLE [dbo].[Hosts] (
    [HostId]   INT            IDENTITY (1, 1) NOT NULL,
    [HostName] NVARCHAR (255) NOT NULL,
    [IsHtml]   BIT            CONSTRAINT [DF_Host_IsHtml] DEFAULT ((0)) NOT NULL,
    [IsCss]    BIT            CONSTRAINT [DF_Host_IsCss] DEFAULT ((1)) NOT NULL,
    [IsJs]     BIT            CONSTRAINT [DF_Host_IsJs] DEFAULT ((1)) NOT NULL,
    [IsJson]   BIT            CONSTRAINT [DF_Host_IsJson] DEFAULT ((1)) NOT NULL,
    [IsXml]    BIT            CONSTRAINT [DF_Host_IsXml] DEFAULT ((1)) NOT NULL,
    [IsOther]  BIT            CONSTRAINT [DF_Host_IsOther] DEFAULT ((1)) NOT NULL,
    CONSTRAINT [PK_Hosts] PRIMARY KEY NONCLUSTERED ([HostId] ASC)
);
GO
CREATE UNIQUE CLUSTERED INDEX [CI_Hosts]
    ON [dbo].[Hosts]([HostName] ASC);
