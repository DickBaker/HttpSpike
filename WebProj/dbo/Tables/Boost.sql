CREATE TABLE [dbo].[Boost] (
    [Scheme]    CHAR (1) NOT NULL,
    [Threshold] INT      NOT NULL,
    [Priority]  TINYINT  NOT NULL,
    CONSTRAINT [PK_Boost] PRIMARY KEY CLUSTERED ([Scheme] ASC, [Threshold] ASC),
    CONSTRAINT [CK_Scheme] CHECK ([Scheme]='H' OR [Scheme]='W')
);

