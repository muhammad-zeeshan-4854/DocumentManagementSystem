namespace DocumentManagement.Web.Models;

// DATETIME2 comes back without a kind, so the setters mark dates as UTC.

public class DocumentListModel
{
    private DateTime dtDateAdded;

    public int DocumentID { get; set; }
    public string Title { get; set; } = "";
    public DateTime DateAdded { get => dtDateAdded; set => dtDateAdded = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
    public string OriginalFileName { get; set; } = "";
    public string FileExtension { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public int NoteCount { get; set; }
    public int TotalCount { get; set; }
}

public class DocumentDetailModel
{
    private DateTime dtDateAdded;

    public int DocumentID { get; set; }
    public string Title { get; set; } = "";
    public DateTime DateAdded { get => dtDateAdded; set => dtDateAdded = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
    public string OriginalFileName { get; set; } = "";
    public string FileExtension { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }
}

public class DocumentNoteModel
{
    private DateTime dtDateAdded;

    public int NoteID { get; set; }
    public int DocumentID { get; set; }
    public string NoteText { get; set; } = "";
    public decimal PositionTop { get; set; }
    public decimal PositionLeft { get; set; }
    public DateTime DateAdded { get => dtDateAdded; set => dtDateAdded = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}

// Server side only, never sent to the browser.
public class DocumentFileModel
{
    public int DocumentID { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string FilePath { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string FileBasePath { get; set; } = "";
}

public class FileDownloadModel
{
    public required string OriginalFileName { get; init; }
    public required string ContentType { get; init; }
    public required Stream Content { get; init; }
}

public class InsertDocumentResultModel
{
    public int DocumentID { get; set; }
    public string FilePath { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string FileBasePath { get; set; } = "";
}

public class ParametersModel
{
    public string SystemName { get; set; } = "";
    public string FileBasePath { get; set; } = "";
    public long MaxUploadSizeBytes { get; set; }
    public int DefaultPageSize { get; set; }
    public int MaxPageSize { get; set; }
    public int MaxTitleLength { get; set; }
    public int MaxNoteLength { get; set; }
    public int DefaultCompanyID { get; set; }
}

public enum FileAccessType
{
    View,
    Download
}

public class AuditLogModel
{
    private DateTime dtDateAdded;

    public long AuditLogID { get; set; }
    public int? DocumentID { get; set; }
    public string ActionType { get; set; } = "";
    public string? ActionDetail { get; set; }
    public DateTime DateAdded { get => dtDateAdded; set => dtDateAdded = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
    public int TotalCount { get; set; }
    public string? DocumentTitle { get; set; }
    public bool IsDocumentAvailable { get; set; }
}

public class CompanyModel
{
    public int CompanyID { get; set; }
    public string CompanyName { get; set; } = "";
}

public class DeleteDocumentResultModel
{
    private DateTime dtDeletedDate;

    public int DocumentID { get; set; }
    public DateTime DeletedDate { get => dtDeletedDate; set => dtDeletedDate = DateTime.SpecifyKind(value, DateTimeKind.Utc); }
}

public class AllowedFileTypeModel
{
    public string FileExtension { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long MaxFileSizeBytes { get; set; }
}

public class FileCheckResultModel
{
    public byte[] FileHeader { get; init; } = [];
    public bool IsCorrupt { get; init; }
    public bool IsPasswordProtected { get; init; }
    public bool HasActiveContent { get; init; }
}

public class PagedListModel<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
