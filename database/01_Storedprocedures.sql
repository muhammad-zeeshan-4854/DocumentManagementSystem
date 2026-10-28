-- 28/10/2026 - MZ - Created - Writes the error being handled to ErrorLog. Call from a CATCH block after ROLLBACK.
CREATE OR ALTER PROCEDURE [dbo].[InsertErrorLog]
@ProcedureParameters NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        INSERT INTO ErrorLog (ErrorNumber, ErrorSeverity, ErrorState, ErrorProcedure, ErrorLine, ErrorMessage, ProcedureParameters)
        VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(), ERROR_LINE(), ERROR_MESSAGE(), @ProcedureParameters);
    END TRY
    BEGIN CATCH
        PRINT 'InsertErrorLog failed: ' + ERROR_MESSAGE();
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - Writes one AuditLog row. Called by the other SPs, inside their transaction when they
CREATE OR ALTER PROCEDURE [dbo].[InsertAuditLog]
@CompanyID    INT,
@DocumentID   INT = NULL,
@NoteID       INT = NULL,
@ActionType   NVARCHAR(30),
@ActionDetail NVARCHAR(MAX) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO AuditLog (CompanyId, DocumentId, NoteId, ActionType, ActionDetail)
    VALUES (@CompanyID, @DocumentID, @NoteID, @ActionType, LEFT(@ActionDetail, 1000));
END
GO

-- 28/10/2026 - MZ - Created - System settings.
CREATE OR ALTER PROCEDURE [dbo].[GetParameters]
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        SELECT SystemName, FileBasePath, MaxUploadSizeBytes, DefaultPageSize, MaxPageSize, MaxTitleLength, MaxNoteLength, DefaultCompanyID
        FROM Parameters WITH (NOLOCK)
        WHERE ParametersID = 1;
    END TRY
    BEGIN CATCH
        EXEC InsertErrorLog @ProcedureParameters = NULL;
        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - Active companies for the company picker, by name.
CREATE OR ALTER PROCEDURE [dbo].[GetCompanyList]
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        SELECT Id AS CompanyID, CompanyName FROM Company WITH (NOLOCK)
        WHERE IsActive = 1
        ORDER BY CompanyName;
    END TRY
    BEGIN CATCH
        EXEC InsertErrorLog @ProcedureParameters = NULL;
        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - Accepted file types and size limits, shown on the upload page.
CREATE OR ALTER PROCEDURE [dbo].[GetAllowedFileTypeList]
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        SELECT FileExtension, ContentType, MaxFileSizeBytes FROM AllowedFileType WITH (NOLOCK)
        WHERE IsActive = 1
        ORDER BY FileExtension;
    END TRY
    BEGIN CATCH
        EXEC InsertErrorLog @ProcedureParameters = NULL;
        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - Validates an upload and creates the Document and DocumentFile rows.
CREATE OR ALTER PROCEDURE [dbo].[InsertDocument]
@CompanyID           INT,
@Title               NVARCHAR(4000),
@OriginalFileName    NVARCHAR(4000),
@ContentType         NVARCHAR(200),
@FileSizeBytes       BIGINT,
@FileHeader          VARBINARY(8),
@IsCorrupt           BIT,
@IsPasswordProtected BIT,
@HasActiveContent    BIT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Parameters NVARCHAR(MAX) = CONCAT(
        N'@CompanyID=', ISNULL(CONVERT(NVARCHAR(20), @CompanyID), N'NULL'),
        N', @Title=', ISNULL(LEFT(@Title, 250), N'NULL'),
        N', @OriginalFileName=', ISNULL(LEFT(@OriginalFileName, 300), N'NULL'),
        N', @ContentType=', ISNULL(LEFT(@ContentType, 200), N'NULL'),
        N', @FileSizeBytes=', ISNULL(CONVERT(NVARCHAR(20), @FileSizeBytes), N'NULL'),
        N', @FileHeader=', ISNULL(CONVERT(NVARCHAR(20), @FileHeader, 1), N'NULL'),
        N', @IsCorrupt=', ISNULL(CONVERT(NVARCHAR(1), @IsCorrupt), N'NULL'),
        N', @IsPasswordProtected=', ISNULL(CONVERT(NVARCHAR(1), @IsPasswordProtected), N'NULL'),
        N', @HasActiveContent=', ISNULL(CONVERT(NVARCHAR(1), @HasActiveContent), N'NULL'));

    BEGIN TRY
        DECLARE @CleanTitle NVARCHAR(4000) = TRIM(@Title), @CleanFileName NVARCHAR(4000) = TRIM(@OriginalFileName), @CleanContentType NVARCHAR(200)  = LOWER(TRIM(@ContentType)),
                @FileExtension NVARCHAR(4000), @AllowedContentType NVARCHAR(100), @AlternateContentType NVARCHAR(100), @FileSignature VARBINARY(8), @MaxFileSizeBytes BIGINT,
                @FileBasePath NVARCHAR(500), @MaxUploadSizeBytes BIGINT, @MaxTitleLength INT, @SizeLimit BIGINT, @FilePath NVARCHAR(200),@StoredFileName NVARCHAR(60),
				@DocumentID INT, @Message NVARCHAR(2048), @Now DATETIME2(3) = SYSUTCDATETIME();

        -- Some browsers send the full client path; keep only the part after the last \ or /.
        SET @CleanFileName = RIGHT(@CleanFileName, CHARINDEX(N'\', REVERSE(REPLACE(@CleanFileName, N'/', N'\')) + N'\') - 1);

        IF CHARINDEX(N';', @CleanContentType) > 0
            SET @CleanContentType = RTRIM(LEFT(@CleanContentType, CHARINDEX(N';', @CleanContentType) - 1));

        SELECT @FileBasePath = FileBasePath, @MaxUploadSizeBytes = MaxUploadSizeBytes, @MaxTitleLength = MaxTitleLength
        FROM Parameters WITH (NOLOCK)
        WHERE ParametersID = 1;

        IF @FileBasePath IS NULL
            THROW 51000, N'Parameters row is missing. Run 01_Schema.sql or insert the Parameters row.', 1;

        IF NOT EXISTS (SELECT 1 FROM Company WITH (NOLOCK) WHERE Id = @CompanyID AND IsActive = 1)
            THROW 50400, N'A valid company is required.', 1;

        IF ISNULL(@CleanTitle, N'') = N''
            THROW 50400, N'Title is required.', 1;

        IF LEN(@CleanTitle) > @MaxTitleLength
        BEGIN
            SET @Message = CONCAT(N'Title must be ', @MaxTitleLength, N' characters or fewer.');
            THROW 50400, @Message, 1;
        END

        IF ISNULL(@CleanFileName, N'') = N''
            THROW 50400, N'Please choose a file to upload.', 1;

        IF LEN(@CleanFileName) > 260
            THROW 50400, N'File name must be 260 characters or fewer.', 1;

        IF ISNULL(@FileSizeBytes, 0) <= 0
            THROW 50400, N'The selected file is empty.', 1;

        IF CHARINDEX(N'.', @CleanFileName) = 0
            THROW 50400, N'The file has no extension, so its type cannot be checked.', 1;

        SET @FileExtension = LOWER(RIGHT(@CleanFileName, CHARINDEX(N'.', REVERSE(@CleanFileName))));

        SELECT @AllowedContentType = ContentType, @AlternateContentType = AlternateContentType, @FileSignature  = FileSignature, @MaxFileSizeBytes = MaxFileSizeBytes
        FROM AllowedFileType WITH (NOLOCK)
        WHERE FileExtension = @FileExtension
          AND IsActive = 1;

        IF @AllowedContentType IS NULL
        BEGIN
            SET @Message = CONCAT(N'Files of type "', LEFT(@FileExtension, 20), N'" are not allowed.');
            THROW 50400, @Message, 1;
        END

        -- One size check for both limits: the overall upload limit first, then the limit for this file type.
        IF @FileSizeBytes > @MaxUploadSizeBytes
            SELECT @SizeLimit = @MaxUploadSizeBytes,
                   @Message   = N'The file is too large. The maximum upload size is ';
        ELSE IF @FileSizeBytes > @MaxFileSizeBytes
            SELECT @SizeLimit = @MaxFileSizeBytes,
                   @Message   = CONCAT(N'The file is too large. The maximum size for ', @FileExtension, N' files is ');

        IF @SizeLimit IS NOT NULL
        BEGIN
            SET @Message = CONCAT(@Message,
                                  CASE WHEN @SizeLimit < 1048576 THEN CONCAT(@SizeLimit / 1024, N' KB')
                                       ELSE CONCAT(@SizeLimit / 1048576, N' MB') END, N'.');
            THROW 50400, @Message, 1;
        END

        -- The browser's MIME type must agree with the extension.
        IF ISNULL(@CleanContentType, N'') NOT IN (@AllowedContentType, ISNULL(@AlternateContentType, @AllowedContentType))
        BEGIN
            SET @Message = CONCAT(N'The file does not look like a ', @FileExtension, N' file. Please upload the original file.');
            THROW 50400, @Message, 1;
        END

        -- Checked before the signature: Office wraps a password protected .docx/.xlsx in a different container.
        IF @IsPasswordProtected = 1
            THROW 50400, N'The file is password protected. Please remove the password and upload it again.', 1;

        -- The file content must start with the signature of its type, so a renamed .exe is rejected.
        IF @FileSignature IS NOT NULL
           AND (ISNULL(DATALENGTH(@FileHeader), 0) < DATALENGTH(@FileSignature)
                OR SUBSTRING(@FileHeader, 1, DATALENGTH(@FileSignature)) <> @FileSignature)
        BEGIN
            SET @Message = CONCAT(N'The file content is not a valid ', @FileExtension, N' file.');
            THROW 50400, @Message, 1;
        END

        IF ISNULL(@IsCorrupt, 1) = 1
            THROW 50400, N'The file is damaged or incomplete and cannot be opened. Please check the file and upload it again.', 1;

        IF ISNULL(@HasActiveContent, 1) = 1
            THROW 50400, N'The file contains scripts, macros or embedded files, so it cannot be uploaded.', 1;

        -- One folder per company per month keeps folders small: {CompanyID}/{yyyy}/{MM}
        -- Style 111 is yyyy/mm/dd, so the first 7 characters are yyyy/mm.
        SET @FilePath       = CONCAT(@CompanyID, N'/', CONVERT(NCHAR(7), @Now, 111));
        SET @StoredFileName = CONCAT(LOWER(CONVERT(NVARCHAR(36), NEWID())), @FileExtension);

        BEGIN TRANSACTION;

            INSERT INTO Document (CompanyId, Title, DateAdded)
            VALUES (@CompanyID, @CleanTitle, @Now);

            SET @DocumentID = SCOPE_IDENTITY();

            INSERT INTO DocumentFile (DocumentId, OriginalFileName, FileExtension, ContentType, FileSizeBytes, FilePath, StoredFileName)
            VALUES (@DocumentID, @CleanFileName, @FileExtension, @AllowedContentType, @FileSizeBytes, @FilePath, @StoredFileName);

            SET @Message = CONCAT(N'Uploaded "', @CleanTitle, N'" (', @CleanFileName, N', ', @FileSizeBytes, N' bytes)');
            EXEC InsertAuditLog @CompanyID = @CompanyID, @DocumentID = @DocumentID, @ActionType = N'DocumentUploaded', @ActionDetail = @Message;

        COMMIT TRANSACTION;

        SELECT @DocumentID AS DocumentID, @FilePath AS FilePath, @StoredFileName AS StoredFileName, @FileBasePath AS FileBasePath;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        IF ERROR_NUMBER() NOT IN (50400, 50404, 50409)
            EXEC InsertErrorLog @ProcedureParameters = @Parameters;

        THROW;
    END CATCH
END
GO
GO

-- 28/10/2026 - MZ - Created - Undo for InsertDocument when the file could not be saved to disk.
CREATE OR ALTER PROCEDURE [dbo].[DeleteFailedUploadDocument]
@DocumentID INT,
@CompanyID  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Parameters NVARCHAR(MAX) = CONCAT(
        N'@DocumentID=', ISNULL(CONVERT(NVARCHAR(20), @DocumentID), N'NULL'),
        N', @CompanyID=', ISNULL(CONVERT(NVARCHAR(20), @CompanyID), N'NULL'));

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM Document WITH (NOLOCK) WHERE Id = @DocumentID AND CompanyId = @CompanyID)
            THROW 50404, N'Document not found.', 1;

        IF EXISTS (SELECT 1 FROM DocumentNote  WITH (NOLOCK) WHERE DocumentId = @DocumentID)
            THROW 50409, N'Document already has notes, so it cannot be removed as a failed upload.', 1;

        DECLARE @Detail NVARCHAR(1000) = (SELECT CONCAT(N'Upload of "', D.Title, N'" (', F.OriginalFileName, N') undone: the file could not be saved')
                                          FROM Document D INNER JOIN DocumentFile F ON F.DocumentId = D.Id
                                          WHERE D.Id = @DocumentID);

        BEGIN TRANSACTION;

            DELETE FROM DocumentFile WHERE DocumentId = @DocumentID;
            DELETE FROM Document WHERE Id = @DocumentID AND CompanyId = @CompanyID;

            EXEC InsertAuditLog @CompanyID = @CompanyID, @DocumentID = @DocumentID, @ActionType = N'UploadFailed', @ActionDetail = @Detail;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        IF ERROR_NUMBER() NOT IN (50400, 50404, 50409)
            EXEC InsertErrorLog @ProcedureParameters = @Parameters;

        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - One page of a company's active documents, newest first, optionally searched by title.
CREATE OR ALTER PROCEDURE [dbo].[GetDocumentList]
@CompanyID  INT,
@Search     NVARCHAR(4000) = NULL,
@PageNumber INT = 1,
@PageSize   INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Parameters NVARCHAR(MAX) = CONCAT(
        N'@CompanyID=', ISNULL(CONVERT(NVARCHAR(20), @CompanyID), N'NULL'),
        N', @Search=', ISNULL(LEFT(@Search, 250), N'NULL'),
        N', @PageNumber=', ISNULL(CONVERT(NVARCHAR(20), @PageNumber), N'NULL'),
        N', @PageSize=', ISNULL(CONVERT(NVARCHAR(20), @PageSize), N'NULL'));

    BEGIN TRY
        DECLARE @CleanSearch   NVARCHAR(4000) = NULLIF(LTRIM(RTRIM(@Search)), N'');
        DECLARE @SearchPattern NVARCHAR(4000);
        DECLARE @MaxPageSize   INT;
        DECLARE @MaxTitleLength INT;
        DECLARE @Message       NVARCHAR(2048);

        SELECT @MaxPageSize = MaxPageSize, @MaxTitleLength = MaxTitleLength
        FROM Parameters WITH (NOLOCK)
        WHERE ParametersID = 1;

        IF @MaxPageSize IS NULL
            THROW 51000, N'Parameters row is missing. Run 01_Schema.sql or insert the Parameters row.', 1;

        IF NOT EXISTS (SELECT 1 FROM Company WITH (NOLOCK) WHERE Id = @CompanyID AND IsActive = 1)
            THROW 50400, N'A valid company is required.', 1;

        IF @PageNumber IS NULL OR @PageNumber < 1
            THROW 50400, N'Page number must be 1 or greater.', 1;

        IF @PageSize IS NULL OR @PageSize < 1 OR @PageSize > @MaxPageSize
        BEGIN
            SET @Message = CONCAT(N'Page size must be between 1 and ', @MaxPageSize, N'.');
            THROW 50400, @Message, 1;
        END

        -- The search is on the title, so it can't be longer than a title.
        IF LEN(@CleanSearch) > @MaxTitleLength
        BEGIN
            SET @Message = CONCAT(N'Search text must be ', @MaxTitleLength, N' characters or fewer.');
            THROW 50400, @Message, 1;
        END

        -- Search is plain text: escape the LIKE wildcards the user typed.
        IF @CleanSearch IS NOT NULL
            SET @SearchPattern = N'%' + REPLACE(REPLACE(REPLACE(@CleanSearch, N'[', N'[[]'), N'%', N'[%]'), N'_', N'[_]') + N'%';

        ;WITH DocumentPage AS
        (
            SELECT D.Id, D.Title, D.DateAdded,COUNT(*) OVER () AS TotalCount
            FROM Document D WITH (NOLOCK)
            WHERE D.CompanyId = @CompanyID AND D.DeletedDate IS NULL
              AND (@SearchPattern IS NULL OR D.Title LIKE @SearchPattern)
            ORDER BY D.DateAdded DESC, D.Id DESC
            OFFSET (@PageNumber - 1) * @PageSize ROWS
            FETCH NEXT @PageSize ROWS ONLY
        )
        SELECT DP.Id AS DocumentID,
               DP.Title,
               DP.DateAdded,
               F.OriginalFileName,
               F.FileExtension,
               F.FileSizeBytes,
               (SELECT COUNT(*) FROM DocumentNote N WITH (NOLOCK) WHERE N.DocumentId = DP.Id) AS NoteCount,
               DP.TotalCount
        FROM DocumentPage DP WITH (NOLOCK)
        INNER JOIN DocumentFile F ON F.DocumentId = DP.Id
        ORDER BY DP.DateAdded DESC, DP.Id DESC
        OPTION (RECOMPILE);  -- a plan per search / no-search case, so the filtered index is always used
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (50400, 50404, 50409)
            EXEC InsertErrorLog @ProcedureParameters = @Parameters;

        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - One active document of a company and its notes, for the details page.
CREATE OR ALTER PROCEDURE [dbo].[GetDocumentByID]
@DocumentID INT,
@CompanyID  INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Parameters NVARCHAR(MAX) = CONCAT(
        N'@DocumentID=', ISNULL(CONVERT(NVARCHAR(20), @DocumentID), N'NULL'),
        N', @CompanyID=', ISNULL(CONVERT(NVARCHAR(20), @CompanyID), N'NULL'));

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM Company WITH (NOLOCK) WHERE Id = @CompanyID AND IsActive = 1)
            THROW 50400, N'A valid company is required.', 1;

        IF NOT EXISTS (SELECT 1 FROM Document WITH (NOLOCK)
                       WHERE Id = @DocumentID AND CompanyId = @CompanyID AND DeletedDate IS NULL)
            THROW 50404, N'Document not found.', 1;

        SELECT D.Id AS DocumentID,
               D.Title,
               D.DateAdded,
               F.OriginalFileName,
               F.FileExtension,
               F.ContentType,
               F.FileSizeBytes
        FROM Document D WITH (NOLOCK)
        INNER JOIN DocumentFile F ON F.DocumentId = D.Id
        WHERE D.Id = @DocumentID;

        SELECT N.Id AS NoteID, N.DocumentId AS DocumentID, N.NoteText, N.PositionTop, N.PositionLeft, N.DateAdded
        FROM DocumentNote N WITH (NOLOCK)
        WHERE N.DocumentId = @DocumentID
        ORDER BY N.Id;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (50400, 50404, 50409)
            EXEC InsertErrorLog @ProcedureParameters = @Parameters;

        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - Where a document's file is stored, for the view and download actions.
CREATE OR ALTER PROCEDURE [dbo].[GetDocumentFileByID]
@DocumentID INT,
@CompanyID  INT,
@AccessType NVARCHAR(20)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Parameters NVARCHAR(MAX) = CONCAT(
        N'@DocumentID=', ISNULL(CONVERT(NVARCHAR(20), @DocumentID), N'NULL'),
        N', @CompanyID=', ISNULL(CONVERT(NVARCHAR(20), @CompanyID), N'NULL'),
        N', @AccessType=', ISNULL(@AccessType, N'NULL'));

    BEGIN TRY
        DECLARE @Detail NVARCHAR(1000);
        DECLARE @ActionType NVARCHAR(30);

        IF NOT EXISTS (SELECT 1 FROM Company WITH (NOLOCK) WHERE Id = @CompanyID AND IsActive = 1)
            THROW 50400, N'A valid company is required.', 1;

        IF @AccessType IS NULL OR @AccessType NOT IN (N'View', N'Download')
            THROW 50400, N'Access type must be View or Download.', 1;

        SELECT @Detail = CONCAT(CASE @AccessType WHEN N'View' THEN N'Viewed "' ELSE N'Downloaded "' END,
                                D.Title, N'" (', F.OriginalFileName, N')')
        FROM Document D WITH (NOLOCK)
        INNER JOIN DocumentFile F ON F.DocumentId = D.Id
        WHERE D.Id = @DocumentID AND D.CompanyId = @CompanyID AND D.DeletedDate IS NULL;

        IF @Detail IS NULL
            THROW 50404, N'Document not found.', 1;

        SET @ActionType = CASE @AccessType WHEN N'View' THEN N'DocumentViewed' ELSE N'DocumentDownloaded' END;
        EXEC InsertAuditLog @CompanyID = @CompanyID, @DocumentID = @DocumentID, @ActionType = @ActionType, @ActionDetail = @Detail;

        SELECT F.DocumentId AS DocumentID,
               F.OriginalFileName,
               F.ContentType,
               F.FileSizeBytes,
               F.FilePath,
               F.StoredFileName,
               P.FileBasePath
        FROM DocumentFile F WITH (NOLOCK)
        CROSS JOIN Parameters P WITH (NOLOCK)
        WHERE F.DocumentId = @DocumentID
          AND P.ParametersID = 1;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (50400, 50404, 50409)
            EXEC InsertErrorLog @ProcedureParameters = @Parameters;

        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - Soft delete: sets DeletedDate and keeps the row, file and notes.
CREATE OR ALTER PROCEDURE [dbo].[DeleteDocumentByID]
@DocumentID INT,
@CompanyID  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Parameters NVARCHAR(MAX) = CONCAT(
        N'@DocumentID=', ISNULL(CONVERT(NVARCHAR(20), @DocumentID), N'NULL'),
        N', @CompanyID=', ISNULL(CONVERT(NVARCHAR(20), @CompanyID), N'NULL'));

    BEGIN TRY
        DECLARE @DeletedDate DATETIME2(3);
        DECLARE @Detail NVARCHAR(1000);

        IF NOT EXISTS (SELECT 1 FROM Company WITH (NOLOCK) WHERE Id = @CompanyID AND IsActive = 1)
            THROW 50400, N'A valid company is required.', 1;

        BEGIN TRANSACTION;

            -- UPDLOCK so two deletes of the same document at the same time can't both succeed.
            SELECT @DeletedDate = DeletedDate, @Detail = CONCAT(N'Deleted "', Title, N'"')
            FROM Document WITH (UPDLOCK, HOLDLOCK)
            WHERE Id = @DocumentID AND CompanyId = @CompanyID;

            IF @@ROWCOUNT = 0
                THROW 50404, N'Document not found.', 1;

            IF @DeletedDate IS NOT NULL
                THROW 50409, N'Document has already been deleted.', 1;

            SET @DeletedDate = SYSUTCDATETIME();

            UPDATE Document
            SET DeletedDate = @DeletedDate
            WHERE Id = @DocumentID AND CompanyId = @CompanyID;

            EXEC InsertAuditLog @CompanyID = @CompanyID, @DocumentID = @DocumentID, @ActionType = N'DocumentDeleted', @ActionDetail = @Detail;

        COMMIT TRANSACTION;

        SELECT @DocumentID AS DocumentID, @DeletedDate AS DeletedDate;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        IF ERROR_NUMBER() NOT IN (50400, 50404, 50409)
            EXEC InsertErrorLog @ProcedureParameters = @Parameters;

        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - Adds a note at a position on an active document and returns it.
CREATE OR ALTER PROCEDURE [dbo].[InsertDocumentNote]
@DocumentID   INT,
@CompanyID    INT,
@NoteText     NVARCHAR(MAX),
@PositionTop  DECIMAL(18, 4),
@PositionLeft DECIMAL(18, 4)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Parameters NVARCHAR(MAX) = CONCAT(
        N'@DocumentID=', ISNULL(CONVERT(NVARCHAR(20), @DocumentID), N'NULL'),
        N', @CompanyID=', ISNULL(CONVERT(NVARCHAR(20), @CompanyID), N'NULL'),
        N', @NoteText(length)=', ISNULL(CONVERT(NVARCHAR(20), LEN(@NoteText)), N'NULL'),
        N', @PositionTop=', ISNULL(CONVERT(NVARCHAR(30), @PositionTop), N'NULL'),
        N', @PositionLeft=', ISNULL(CONVERT(NVARCHAR(30), @PositionLeft), N'NULL'));

    BEGIN TRY
        DECLARE @CleanNoteText NVARCHAR(MAX) = LTRIM(RTRIM(@NoteText));
        DECLARE @NoteID INT;
        DECLARE @DocumentName NVARCHAR(500);
        DECLARE @MaxNoteLength INT = (SELECT MaxNoteLength FROM  Parameters WITH (NOLOCK) WHERE ParametersID = 1);
        DECLARE @Message NVARCHAR(2048);

        IF @MaxNoteLength IS NULL
            THROW 51000, N'Parameters row is missing. Run 01_Schema.sql or insert the Parameters row.', 1;

        IF NOT EXISTS (SELECT 1 FROM Company WITH (NOLOCK) WHERE Id = @CompanyID AND IsActive = 1)
            THROW 50400, N'A valid company is required.', 1;

        IF @CleanNoteText IS NULL OR @CleanNoteText = N''
            THROW 50400, N'Note text is required.', 1;

        IF LEN(@CleanNoteText) > @MaxNoteLength
        BEGIN
            SET @Message = CONCAT(N'Note text must be ', @MaxNoteLength, N' characters or fewer.');
            THROW 50400, @Message, 1;
        END

        IF @PositionTop IS NULL OR @PositionTop < 0 OR @PositionTop > 100
            THROW 50400, N'Note position (top) must be between 0 and 100.', 1;

        IF @PositionLeft IS NULL OR @PositionLeft < 0 OR @PositionLeft > 100
            THROW 50400, N'Note position (left) must be between 0 and 100.', 1;

        BEGIN TRANSACTION;

            -- Lock the document row so it can't be deleted while the note is being added.
            SELECT @DocumentName = CONCAT(N'"', D.Title, N'" (', F.OriginalFileName, N')')
            FROM Document D WITH (UPDLOCK, HOLDLOCK)
            INNER JOIN DocumentFile F ON F.DocumentId = D.Id
            WHERE D.Id = @DocumentID AND D.CompanyId = @CompanyID AND D.DeletedDate IS NULL;

            IF @DocumentName IS NULL
                THROW 50404, N'Document not found. It may have been deleted.', 1;

            INSERT INTO DocumentNote (DocumentId, NoteText, PositionTop, PositionLeft)
            VALUES (@DocumentID, @CleanNoteText, ROUND(@PositionTop, 4), ROUND(@PositionLeft, 4));

            SET @NoteID = SCOPE_IDENTITY();

            SET @Message = CONCAT(N'Note added on ', @DocumentName, N': "', LEFT(@CleanNoteText, 400), N'"');
            EXEC InsertAuditLog @CompanyID = @CompanyID, @DocumentID = @DocumentID, @NoteID = @NoteID, @ActionType = N'NoteAdded', @ActionDetail = @Message;

        COMMIT TRANSACTION;

        SELECT N.Id AS NoteID, N.DocumentId AS DocumentID, N.NoteText, N.PositionTop, N.PositionLeft, N.DateAdded
        FROM DocumentNote N WITH (NOLOCK)
        WHERE N.Id = @NoteID;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        IF ERROR_NUMBER() NOT IN (50400, 50404, 50409)
            EXEC InsertErrorLog @ProcedureParameters = @Parameters;

        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - Notes of an active document, oldest first.
CREATE OR ALTER PROCEDURE [dbo].[GetDocumentNotesByDocumentID]
@DocumentID INT,
@CompanyID  INT
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Parameters NVARCHAR(MAX) = CONCAT(
        N'@DocumentID=', ISNULL(CONVERT(NVARCHAR(20), @DocumentID), N'NULL'),
        N', @CompanyID=', ISNULL(CONVERT(NVARCHAR(20), @CompanyID), N'NULL'));

    BEGIN TRY
        IF NOT EXISTS (SELECT 1 FROM Company WITH (NOLOCK) WHERE Id = @CompanyID AND IsActive = 1)
            THROW 50400, N'A valid company is required.', 1;

        IF NOT EXISTS (SELECT 1 FROM Document WITH (NOLOCK)
                       WHERE Id = @DocumentID AND CompanyId = @CompanyID AND DeletedDate IS NULL)
            THROW 50404, N'Document not found.', 1;

        SELECT N.Id AS NoteID, N.DocumentId AS DocumentID, N.NoteText, N.PositionTop, N.PositionLeft, N.DateAdded
        FROM DocumentNote N WITH (NOLOCK)
        WHERE N.DocumentId = @DocumentID
        ORDER BY N.Id;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (50400, 50404, 50409)
            EXEC InsertErrorLog @ProcedureParameters = @Parameters;

        THROW;
    END CATCH
END
GO

-- 28/10/2026 - MZ - Created - One page of a company's audit log, newest first. TotalCount on each row covers all pages.
CREATE OR ALTER PROCEDURE [dbo].[GetAuditLogList]
@CompanyID  INT,
@PageNumber INT = 1,
@PageSize   INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @Parameters NVARCHAR(MAX) = CONCAT(
        N'@CompanyID=', ISNULL(CONVERT(NVARCHAR(20), @CompanyID), N'NULL'),
        N', @PageNumber=', ISNULL(CONVERT(NVARCHAR(20), @PageNumber), N'NULL'),
        N', @PageSize=', ISNULL(CONVERT(NVARCHAR(20), @PageSize), N'NULL'));

    BEGIN TRY
        DECLARE @MaxPageSize INT = (SELECT MaxPageSize FROM Parameters WITH (NOLOCK) WHERE ParametersID = 1);
        DECLARE @Message NVARCHAR(2048);

        IF @MaxPageSize IS NULL
            THROW 51000, N'Parameters row is missing. Run 01_Schema.sql or insert the Parameters row.', 1;

        IF NOT EXISTS (SELECT 1 FROM Company WITH (NOLOCK) WHERE Id = @CompanyID AND IsActive = 1)
            THROW 50400, N'A valid company is required.', 1;

        IF @PageNumber IS NULL OR @PageNumber < 1
            THROW 50400, N'Page number must be 1 or greater.', 1;

        IF @PageSize IS NULL OR @PageSize < 1 OR @PageSize > @MaxPageSize
        BEGIN
            SET @Message = CONCAT(N'Page size must be between 1 and ', @MaxPageSize, N'.');
            THROW 50400, @Message, 1;
        END

        -- Page the audit rows first (index seek on IX_AuditLog_CompanyId_DateAdded), then look up the documents of that page only.
        -- LEFT JOIN: a failed upload removes its Document row but keeps its audit row.
        ;WITH AuditPage AS
        (
            SELECT A.Id, A.DocumentId, A.ActionType, A.ActionDetail, A.DateAdded,
                   COUNT(*) OVER () AS TotalCount
            FROM AuditLog A WITH (NOLOCK)
            WHERE A.CompanyId = @CompanyID
            ORDER BY A.DateAdded DESC, A.Id DESC
            OFFSET (@PageNumber - 1) * @PageSize ROWS
            FETCH NEXT @PageSize ROWS ONLY
        )
        SELECT P.Id AS AuditLogID,
               P.DocumentId AS DocumentID,
               P.ActionType,
               P.ActionDetail,
               P.DateAdded,
               P.TotalCount,
               D.Title AS DocumentTitle,
               CAST(CASE WHEN D.Id IS NOT NULL AND D.DeletedDate IS NULL THEN 1 ELSE 0 END AS BIT) AS IsDocumentAvailable
        FROM AuditPage P WITH (NOLOCK)
        LEFT JOIN Document D ON D.Id = P.DocumentId AND D.CompanyId = @CompanyID
        ORDER BY P.DateAdded DESC, P.Id DESC;
    END TRY
    BEGIN CATCH
        IF ERROR_NUMBER() NOT IN (50400, 50404, 50409)
            EXEC InsertErrorLog @ProcedureParameters = @Parameters;

        THROW;
    END CATCH
END
GO

PRINT 'DocumentManagement schema is up to date.';
GO
