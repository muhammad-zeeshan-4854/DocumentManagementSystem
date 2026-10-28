
/* =========================================================================================
                                          CREATE DataBase
   ========================================================================================= */
IF DB_ID(N'DocumentManagement') IS NULL
BEGIN
    CREATE DATABASE DocumentManagement;
    PRINT 'Database DocumentManagement created.';

END
ELSE
BEGIN
    PRINT 'Database DocumentManagement already exists.';
END
GO
	USE [DocumentManagement]

/* =========================================================================================
                                          Tables
   ========================================================================================= */
-- Companies (tenants). Every document belongs to one; an inactive company can't read or add anything.
IF OBJECT_ID(N'dbo.Company', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Company
    (
        Id          INT IDENTITY(1, 1) NOT NULL,
        CompanyName NVARCHAR(200)      NOT NULL,
        IsActive    BIT                NOT NULL CONSTRAINT DF_Company_IsActive DEFAULT (1),
        DateAdded   DATETIME2(3)       NOT NULL CONSTRAINT DF_Company_DateAdded DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Company PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_Company_CompanyName UNIQUE (CompanyName),
        CONSTRAINT CK_Company_CompanyName CHECK (LEN(LTRIM(RTRIM(CompanyName))) > 0)
    );
END
GO

IF OBJECT_ID(N'dbo.Document', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Document
    (
        Id          INT IDENTITY(1, 1) NOT NULL,
        CompanyId   INT                NOT NULL,
        Title       NVARCHAR(200)      NOT NULL,
        DateAdded   DATETIME2(3)       NOT NULL CONSTRAINT DF_Document_DateAdded DEFAULT (SYSUTCDATETIME()),
        DeletedDate DATETIME2(3)       NULL,
        CONSTRAINT PK_Document PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_Document_Company FOREIGN KEY (CompanyId) REFERENCES dbo.Company (Id),
        CONSTRAINT CK_Document_Title CHECK (LEN(LTRIM(RTRIM(Title))) > 0)
    );
END
GO

IF OBJECT_ID(N'dbo.DocumentFile', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentFile
    (
        DocumentId       INT           NOT NULL,
        OriginalFileName NVARCHAR(260) NOT NULL,
        FileExtension    NVARCHAR(10)  NOT NULL,
        ContentType      NVARCHAR(100) NOT NULL,
        FileSizeBytes    BIGINT        NOT NULL,
        FilePath         NVARCHAR(200) NOT NULL,
        StoredFileName   NVARCHAR(60)  NOT NULL,
        CONSTRAINT PK_DocumentFile PRIMARY KEY CLUSTERED (DocumentId),
        CONSTRAINT FK_DocumentFile_Document FOREIGN KEY (DocumentId) REFERENCES dbo.Document (Id),
        CONSTRAINT UQ_DocumentFile_StoredFileName UNIQUE (StoredFileName),
        CONSTRAINT CK_DocumentFile_FileSizeBytes CHECK (FileSizeBytes > 0)
    );
END
GO

IF OBJECT_ID(N'dbo.DocumentNote', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentNote
    (
        Id           INT IDENTITY(1, 1) NOT NULL,
        DocumentId   INT                NOT NULL,
        NoteText     NVARCHAR(1000)     NOT NULL,
        PositionTop  DECIMAL(7, 4)      NOT NULL,  -- % of the document viewer height (0-100)
        PositionLeft DECIMAL(7, 4)      NOT NULL,  -- % of the document viewer width  (0-100)
        DateAdded    DATETIME2(3)       NOT NULL CONSTRAINT DF_DocumentNote_DateAdded DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_DocumentNote PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_DocumentNote_Document FOREIGN KEY (DocumentId) REFERENCES dbo.Document (Id),
        CONSTRAINT CK_DocumentNote_NoteText CHECK (LEN(LTRIM(RTRIM(NoteText))) > 0),
        CONSTRAINT CK_DocumentNote_PositionTop CHECK (PositionTop BETWEEN 0 AND 100),
        CONSTRAINT CK_DocumentNote_PositionLeft CHECK (PositionLeft BETWEEN 0 AND 100)
    );
END
GO

IF OBJECT_ID(N'dbo.AllowedFileType', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AllowedFileType
    (
        Id                   INT IDENTITY(1, 1) NOT NULL,
        FileExtension        NVARCHAR(10)       NOT NULL,  -- lower case, with the dot, e.g. '.pdf'
        ContentType          NVARCHAR(100)      NOT NULL,
        AlternateContentType NVARCHAR(100)      NULL,      -- another MIME type browsers send for it
        FileSignature        VARBINARY(8)       NULL,
        MaxFileSizeBytes     BIGINT             NOT NULL,
        IsActive             BIT                NOT NULL CONSTRAINT DF_AllowedFileType_IsActive DEFAULT (1),
        CONSTRAINT PK_AllowedFileType PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_AllowedFileType_FileExtension UNIQUE (FileExtension),
        CONSTRAINT CK_AllowedFileType_MaxFileSizeBytes CHECK (MaxFileSizeBytes > 0)
    );
END
GO

-- System settings that can change without a deployment. One row only (ParametersID = 1).
-- FileBasePath: root folder for uploaded files, absolute (e.g. D:\DMSFiles) or relative to the web app folder.
--   Changing it does not move existing files; move the folder contents to the new path as well.
-- MaxUploadSizeBytes: largest upload request the site accepts. Per file type limits are in dbo.AllowedFileType.
-- MaxTitleLength / MaxNoteLength can be lowered freely; they can't go above the column sizes (200 / 1000).
-- DefaultCompanyID: the company used until one is picked in the navigation bar.
IF OBJECT_ID(N'dbo.Parameters', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Parameters
    (
        ParametersID       TINYINT       NOT NULL CONSTRAINT DF_Parameters_ParametersID DEFAULT (1),
        SystemName         NVARCHAR(100) NOT NULL,
        FileBasePath       NVARCHAR(500) NOT NULL,
        MaxUploadSizeBytes BIGINT        NOT NULL,
        DefaultPageSize    INT           NOT NULL,
        MaxPageSize        INT           NOT NULL,
        MaxTitleLength     INT           NOT NULL,
        MaxNoteLength      INT           NOT NULL,
        DefaultCompanyID   INT           NOT NULL,
        RecordUpdated      DATETIME2(3)  NOT NULL CONSTRAINT DF_Parameters_RecordUpdated DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_Parameters PRIMARY KEY CLUSTERED (ParametersID),
        CONSTRAINT FK_Parameters_Company FOREIGN KEY (DefaultCompanyID) REFERENCES dbo.Company (Id),
        CONSTRAINT CK_Parameters_SingleRow CHECK (ParametersID = 1),
        CONSTRAINT CK_Parameters_SystemName CHECK (LEN(LTRIM(RTRIM(SystemName))) > 0),
        CONSTRAINT CK_Parameters_FileBasePath CHECK (LEN(LTRIM(RTRIM(FileBasePath))) > 0),
        CONSTRAINT CK_Parameters_MaxUploadSizeBytes CHECK (MaxUploadSizeBytes > 0),
        CONSTRAINT CK_Parameters_PageSize CHECK (MaxPageSize BETWEEN 1 AND 500 AND DefaultPageSize BETWEEN 1 AND MaxPageSize),
        CONSTRAINT CK_Parameters_MaxTitleLength CHECK (MaxTitleLength BETWEEN 1 AND 200),
        CONSTRAINT CK_Parameters_MaxNoteLength CHECK (MaxNoteLength BETWEEN 1 AND 1000)
    );
END
GO

-- History of what happened to documents, written by the SPs inside the same transaction as the change.
-- DocumentId / NoteId have no foreign key on purpose: the history must outlive the rows it describes.
IF OBJECT_ID(N'dbo.AuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLog
    (
        Id           BIGINT IDENTITY(1, 1) NOT NULL,
        CompanyId    INT                   NOT NULL,
        DocumentId   INT                   NULL,
        NoteId       INT                   NULL,
        ActionType   NVARCHAR(30)          NOT NULL,
        ActionDetail NVARCHAR(1000)        NULL,
        DateAdded    DATETIME2(3)          NOT NULL CONSTRAINT DF_AuditLog_DateAdded DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_AuditLog PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_AuditLog_Company FOREIGN KEY (CompanyId) REFERENCES dbo.Company (Id),
        CONSTRAINT CK_AuditLog_ActionType CHECK (ActionType IN (N'DocumentUploaded', N'UploadFailed', N'DocumentViewed',
            N'DocumentDownloaded', N'DocumentDeleted', N'NoteAdded'))
    );
END
GO

IF OBJECT_ID(N'dbo.ErrorLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ErrorLog
    (
        Id                  BIGINT IDENTITY(1, 1) NOT NULL,
        ErrorNumber         INT                   NULL,
        ErrorSeverity       INT                   NULL,
        ErrorState          INT                   NULL,
        ErrorProcedure      NVARCHAR(128)         NULL,
        ErrorLine           INT                   NULL,
        ErrorMessage        NVARCHAR(4000)        NULL,
        ProcedureParameters NVARCHAR(MAX)         NULL,
        DateAdded           DATETIME2(3)          NOT NULL CONSTRAINT DF_ErrorLog_DateAdded DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ErrorLog PRIMARY KEY CLUSTERED (Id)
    );
END
GO

/* =========================================================================================
                                          INDEXES
   ========================================================================================= */

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Document_CompanyId_DateAdded' AND object_id = OBJECT_ID(N'dbo.Document'))
    CREATE NONCLUSTERED INDEX IX_Document_CompanyId_DateAdded
        ON dbo.Document (CompanyId, DateAdded DESC, Id DESC)
        INCLUDE (Title)
        WHERE DeletedDate IS NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_DocumentNote_DocumentId' AND object_id = OBJECT_ID(N'dbo.DocumentNote'))
    CREATE NONCLUSTERED INDEX IX_DocumentNote_DocumentId
        ON dbo.DocumentNote (DocumentId)
        INCLUDE (NoteText, PositionTop, PositionLeft, DateAdded);
GO

-- GetAuditLogList: a company's history, newest first.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AuditLog_CompanyId_DateAdded' AND object_id = OBJECT_ID(N'dbo.AuditLog'))
    CREATE NONCLUSTERED INDEX IX_AuditLog_CompanyId_DateAdded
        ON dbo.AuditLog (CompanyId, DateAdded DESC, Id DESC);
GO

-- dbo.DocumentFile is looked up by its primary key (DocumentId); dbo.AllowedFileType by its unique FileExtension.

/* =========================================================================================
                                            INSERT DATA
   ========================================================================================= */

MERGE dbo.AllowedFileType AS T
USING (VALUES
    (N'.pdf',  N'application/pdf',                                                         NULL,                  0x25504446,         CAST(20971520 AS BIGINT)),
    (N'.png',  N'image/png',                                                               NULL,                  0x89504E470D0A1A0A, CAST(10485760 AS BIGINT)),
    (N'.jpg',  N'image/jpeg',                                                              NULL,                  0xFFD8FF,           CAST(10485760 AS BIGINT)),
    (N'.jpeg', N'image/jpeg',                                                              NULL,                  0xFFD8FF,           CAST(10485760 AS BIGINT)),
    (N'.txt',  N'text/plain',                                                              NULL,                  NULL,               CAST(5242880  AS BIGINT)),
    (N'.docx', N'application/vnd.openxmlformats-officedocument.wordprocessingml.document', N'application/zip',    0x504B0304,         CAST(20971520 AS BIGINT)),
    (N'.xlsx', N'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',       N'application/zip',    0x504B0304,         CAST(20971520 AS BIGINT))
) AS S (FileExtension, ContentType, AlternateContentType, FileSignature, MaxFileSizeBytes)
    ON T.FileExtension = S.FileExtension
WHEN NOT MATCHED BY TARGET THEN
    INSERT (FileExtension, ContentType, AlternateContentType, FileSignature, MaxFileSizeBytes, IsActive)
    VALUES (S.FileExtension, S.ContentType, S.AlternateContentType, S.FileSignature, S.MaxFileSizeBytes, 1);
GO

-- Demo companies. Added only if missing, so renamed or deactivated companies stay as they are.
MERGE dbo.Company AS T
USING (VALUES (N'Tesco'), (N'ASDA'), (N'Northwind Traders')) AS S (CompanyName)
    ON T.CompanyName = S.CompanyName
WHEN NOT MATCHED BY TARGET THEN
    INSERT (CompanyName) VALUES (S.CompanyName);
GO

-- Inserted once; later edits made in the database are kept when this script is run again.
IF NOT EXISTS (SELECT 1 FROM dbo.Parameters)
    INSERT INTO dbo.Parameters (ParametersID, SystemName, FileBasePath, MaxUploadSizeBytes, DefaultPageSize, MaxPageSize, MaxTitleLength, MaxNoteLength, DefaultCompanyID)
    SELECT 1, N'Document Management', N'App_Data/Documents', 26214400, 10, 100, 200, 1000, MIN(Id)
    FROM dbo.Company
    WHERE IsActive = 1;
GO