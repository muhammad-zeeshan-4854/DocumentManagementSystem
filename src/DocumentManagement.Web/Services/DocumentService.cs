using System.Diagnostics;
using DocumentManagement.Web.DataServices;
using DocumentManagement.Web.Helper;
using DocumentManagement.Web.Models;
using DocumentManagement.Web.ViewModels;

namespace DocumentManagement.Web.Services;

public interface IDocumentService
{
    Task<IReadOnlyList<AllowedFileTypeModel>> GetAllowedFileTypeListAsync(CancellationToken oCancellationToken);
    Task<PagedListModel<DocumentListModel>> GetDocumentListAsync(string? strSearch, int iPageNumber, int iPageSize, CancellationToken oCancellationToken);
    Task<int> UploadDocumentAsync(string? strTitle, IFormFile? oFile, CancellationToken oCancellationToken);
    Task<DocumentDetailViewModel> GetDocumentDetailAsync(int iDocumentID, CancellationToken oCancellationToken);
    Task<FileDownloadModel> GetDocumentFileAsync(int iDocumentID, FileAccessType eAccessType, CancellationToken oCancellationToken);
    Task DeleteDocumentAsync(int iDocumentID, CancellationToken oCancellationToken);
    Task<DocumentNoteModel> AddNoteAsync(int iDocumentID, string? strNoteText, decimal? decPositionTop, decimal? decPositionLeft, CancellationToken oCancellationToken);
    Task<IReadOnlyList<DocumentNoteModel>> GetNotesAsync(int iDocumentID, CancellationToken oCancellationToken);
}

public class DocumentService : IDocumentService
{
    private readonly IDocumentDataService oDataService;
    private readonly IFileUtility oFileUtility;
    private readonly ICompanyContext oCompanyContext;
    private readonly ILogger<DocumentService> oLogger;

    public DocumentService(IDocumentDataService oDataService, IFileUtility oFileUtility, ICompanyContext oCompanyContext,
        ILogger<DocumentService> oLogger)
    {
        this.oDataService = oDataService;
        this.oFileUtility = oFileUtility;
        this.oCompanyContext = oCompanyContext;
        this.oLogger = oLogger;
    }

    public Task<IReadOnlyList<AllowedFileTypeModel>> GetAllowedFileTypeListAsync(CancellationToken oCancellationToken)
    {
        return oDataService.GetAllowedFileTypeListAsync(oCancellationToken);
    }

    public async Task<PagedListModel<DocumentListModel>> GetDocumentListAsync(string? strSearch, int iPageNumber, int iPageSize, CancellationToken oCancellationToken)
    {
        int iCompanyID = await oCompanyContext.GetCompanyIDAsync(oCancellationToken);
        return await oDataService.GetDocumentListAsync(iCompanyID, strSearch, iPageNumber, iPageSize, oCancellationToken);
    }

    public async Task<int> UploadDocumentAsync(string? strTitle, IFormFile? oFile, CancellationToken oCancellationToken)
    {
        int iCompanyID = await oCompanyContext.GetCompanyIDAsync(oCancellationToken);
        var oStopwatch = Stopwatch.StartNew();

        FileCheckResultModel? oFileCheck = oFile is { Length: > 0 }
            ? await oFileUtility.CheckFileAsync(oFile, oCancellationToken)
            : null;

        InsertDocumentResultModel oResult = await oDataService.InsertDocumentAsync(iCompanyID, strTitle, oFile?.FileName, oFile?.ContentType,
            oFile?.Length ?? 0, oFileCheck, oCancellationToken);

        // If saving the file fails, remove the row again.
        try
        {
            await using Stream oUploadStream = oFile!.OpenReadStream();
            await oFileUtility.SaveFileAsync(oResult.FileBasePath, oResult.FilePath, oResult.StoredFileName, oUploadStream, oCancellationToken);
        }
        catch (Exception ex)
        {
            oLogger.LogWarning("Saving the file for document {DocumentID} failed ({ErrorType}: {ErrorMessage}); removing the document record",
                oResult.DocumentID, ex.GetType().Name, ex.Message);
            await DeleteFailedUploadAsync(oResult.DocumentID, iCompanyID);
            throw;
        }

        oLogger.LogInformation("Document {DocumentID} uploaded: {FileName} ({FileSizeBytes} bytes) saved as {FilePath}/{StoredFileName} in {ElapsedMs} ms",
            oResult.DocumentID, oFile.FileName, oFile.Length, oResult.FilePath, oResult.StoredFileName, oStopwatch.ElapsedMilliseconds);
        return oResult.DocumentID;
    }

    public async Task<DocumentDetailViewModel> GetDocumentDetailAsync(int iDocumentID, CancellationToken oCancellationToken)
    {
        int iCompanyID = await oCompanyContext.GetCompanyIDAsync(oCancellationToken);
        return await oDataService.GetDocumentByIDAsync(iDocumentID, iCompanyID, oCancellationToken);
    }

    public async Task<FileDownloadModel> GetDocumentFileAsync(int iDocumentID, FileAccessType eAccessType, CancellationToken oCancellationToken)
    {
        int iCompanyID = await oCompanyContext.GetCompanyIDAsync(oCancellationToken);
        DocumentFileModel oFile = await oDataService.GetDocumentFileByIDAsync(iDocumentID, iCompanyID, eAccessType, oCancellationToken);
        return new FileDownloadModel
        {
            OriginalFileName = oFile.OriginalFileName,
            ContentType = oFile.ContentType,
            Content = oFileUtility.OpenFile(oFile.FileBasePath, oFile.FilePath, oFile.StoredFileName)
        };
    }

    public async Task DeleteDocumentAsync(int iDocumentID, CancellationToken oCancellationToken)
    {
        int iCompanyID = await oCompanyContext.GetCompanyIDAsync(oCancellationToken);
        DeleteDocumentResultModel oResult = await oDataService.DeleteDocumentByIDAsync(iDocumentID, iCompanyID, oCancellationToken);
        oLogger.LogInformation("Document {DocumentID} soft deleted at {DeletedDate:o}", iDocumentID, oResult.DeletedDate);
    }

    public async Task<DocumentNoteModel> AddNoteAsync(int iDocumentID, string? strNoteText, decimal? decPositionTop, decimal? decPositionLeft, CancellationToken oCancellationToken)
    {
        int iCompanyID = await oCompanyContext.GetCompanyIDAsync(oCancellationToken);
        DocumentNoteModel oNote = await oDataService.InsertDocumentNoteAsync(iDocumentID, iCompanyID, strNoteText, decPositionTop, decPositionLeft, oCancellationToken);
        oLogger.LogInformation("Note {NoteID} added to document {DocumentID} at ({PositionTop}%, {PositionLeft}%)",
            oNote.NoteID, iDocumentID, oNote.PositionTop, oNote.PositionLeft);
        return oNote;
    }

    public async Task<IReadOnlyList<DocumentNoteModel>> GetNotesAsync(int iDocumentID, CancellationToken oCancellationToken)
    {
        int iCompanyID = await oCompanyContext.GetCompanyIDAsync(oCancellationToken);
        return await oDataService.GetDocumentNotesByDocumentIDAsync(iDocumentID, iCompanyID, oCancellationToken);
    }

    private async Task DeleteFailedUploadAsync(int iDocumentID, int iCompanyID)
    {
        try
        {
            // Must run even if the browser has gone.
            await oDataService.DeleteFailedUploadDocumentAsync(iDocumentID, iCompanyID, CancellationToken.None);
            oLogger.LogInformation("Removed document {DocumentID} after its file could not be saved", iDocumentID);
        }
        catch (Exception ex)
        {
            oLogger.LogError(ex, "Could not remove document {DocumentID} (company {CompanyID}) after a failed upload. " +
                "It has no file; remove it with dbo.DeleteFailedUploadDocument", iDocumentID, iCompanyID);
        }
    }
}
