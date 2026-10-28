using DocumentManagement.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DocumentManagement.Web.ViewModels;

public class DocumentListViewModel
{
    public string? Search { get; init; }
    public PagedListModel<DocumentListModel> Documents { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class AuditLogListViewModel
{
    public PagedListModel<AuditLogModel> AuditLogs { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

// No validation attributes: the rules are in dbo.InsertDocument.
public class UploadDocumentViewModel
{
    public string? Title { get; set; }

    public IFormFile? File { get; set; }

    [BindNever]
    public IReadOnlyList<AllowedFileTypeModel> AllowedFileTypes { get; set; } = [];
}

public class DocumentDetailViewModel
{
    public required DocumentDetailModel Document { get; init; }
    public IReadOnlyList<DocumentNoteModel> Notes { get; init; } = [];
}

public class AddNoteRequestModel
{
    public string? NoteText { get; set; }
    public decimal? PositionTop { get; set; }
    public decimal? PositionLeft { get; set; }
}
