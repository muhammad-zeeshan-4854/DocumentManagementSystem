using Dapper;
using DocumentManagement.Web.Helper;
using DocumentManagement.Web.Models;
using DocumentManagement.Web.ViewModels;

namespace DocumentManagement.Web.DataServices;

public interface IDocumentDataService
{
    Task<IReadOnlyList<AllowedFileTypeModel>> GetAllowedFileTypeListAsync(CancellationToken oCancellationToken);
    Task<InsertDocumentResultModel> InsertDocumentAsync(int iCompanyID, string? strTitle, string? strOriginalFileName, string? strContentType,
        long lFileSizeBytes, FileCheckResultModel? oFileCheck, CancellationToken oCancellationToken);
    Task DeleteFailedUploadDocumentAsync(int iDocumentID, int iCompanyID, CancellationToken oCancellationToken);
    Task<PagedListModel<DocumentListModel>> GetDocumentListAsync(int iCompanyID, string? strSearch, int iPageNumber, int iPageSize, CancellationToken oCancellationToken);
    Task<DocumentDetailViewModel> GetDocumentByIDAsync(int iDocumentID, int iCompanyID, CancellationToken oCancellationToken);
    Task<DocumentFileModel> GetDocumentFileByIDAsync(int iDocumentID, int iCompanyID, FileAccessType eAccessType, CancellationToken oCancellationToken);
    Task<DeleteDocumentResultModel> DeleteDocumentByIDAsync(int iDocumentID, int iCompanyID, CancellationToken oCancellationToken);
    Task<DocumentNoteModel> InsertDocumentNoteAsync(int iDocumentID, int iCompanyID, string? strNoteText, decimal? decPositionTop, decimal? decPositionLeft, CancellationToken oCancellationToken);
    Task<IReadOnlyList<DocumentNoteModel>> GetDocumentNotesByDocumentIDAsync(int iDocumentID, int iCompanyID, CancellationToken oCancellationToken);
}

public class DocumentDataService : IDocumentDataService
{
    private readonly ISqlHelper oSqlHelper;

    public DocumentDataService(ISqlHelper oSqlHelper)
    {
        this.oSqlHelper = oSqlHelper;
    }

    public Task<IReadOnlyList<AllowedFileTypeModel>> GetAllowedFileTypeListAsync(CancellationToken oCancellationToken)
    {
        return oSqlHelper.ExecQueryAsync<AllowedFileTypeModel>("dbo.GetAllowedFileTypeList", new DynamicParameters(), oCancellationToken);
    }

    public Task<InsertDocumentResultModel> InsertDocumentAsync(int iCompanyID, string? strTitle, string? strOriginalFileName, string? strContentType,
        long lFileSizeBytes, FileCheckResultModel? oFileCheck, CancellationToken oCancellationToken)
    {
        var oParam = new DynamicParameters();
        oParam.Add("@CompanyID", iCompanyID);
        oParam.Add("@Title", strTitle);
        oParam.Add("@OriginalFileName", strOriginalFileName);
        oParam.Add("@ContentType", strContentType);
        oParam.Add("@FileSizeBytes", lFileSizeBytes);
        oParam.Add("@FileHeader", oFileCheck?.FileHeader, System.Data.DbType.Binary, size: 8);
        oParam.Add("@IsCorrupt", oFileCheck?.IsCorrupt ?? false);
        oParam.Add("@IsPasswordProtected", oFileCheck?.IsPasswordProtected ?? false);
        oParam.Add("@HasActiveContent", oFileCheck?.HasActiveContent ?? false);
        return oSqlHelper.ExecQuerySingleAsync<InsertDocumentResultModel>("dbo.InsertDocument", oParam, oCancellationToken);
    }

    public Task DeleteFailedUploadDocumentAsync(int iDocumentID, int iCompanyID, CancellationToken oCancellationToken)
    {
        var oParam = new DynamicParameters();
        oParam.Add("@DocumentID", iDocumentID);
        oParam.Add("@CompanyID", iCompanyID);
        return oSqlHelper.ExecNonQueryAsync("dbo.DeleteFailedUploadDocument", oParam, oCancellationToken);
    }

    public async Task<PagedListModel<DocumentListModel>> GetDocumentListAsync(int iCompanyID, string? strSearch, int iPageNumber, int iPageSize, CancellationToken oCancellationToken)
    {
        var oParam = new DynamicParameters();
        oParam.Add("@CompanyID", iCompanyID);
        oParam.Add("@Search", strSearch);
        oParam.Add("@PageNumber", iPageNumber);
        oParam.Add("@PageSize", iPageSize);

        var lstDocuments = await oSqlHelper.ExecQueryAsync<DocumentListModel>("dbo.GetDocumentList", oParam, oCancellationToken);
        return new PagedListModel<DocumentListModel>
        {
            Items = lstDocuments,
            TotalCount = lstDocuments.Count > 0 ? lstDocuments[0].TotalCount : 0,
            PageNumber = iPageNumber,
            PageSize = iPageSize
        };
    }

    public Task<DocumentDetailViewModel> GetDocumentByIDAsync(int iDocumentID, int iCompanyID, CancellationToken oCancellationToken)
    {
        var oParam = new DynamicParameters();
        oParam.Add("@DocumentID", iDocumentID);
        oParam.Add("@CompanyID", iCompanyID);
        return oSqlHelper.ExecQueryMultipleAsync("dbo.GetDocumentByID", oParam, async oMultiReader => new DocumentDetailViewModel
        {
            Document = await oMultiReader.ReadSingleAsync<DocumentDetailModel>(),
            Notes = (await oMultiReader.ReadAsync<DocumentNoteModel>()).AsList()
        }, oCancellationToken);
    }

    public Task<DocumentFileModel> GetDocumentFileByIDAsync(int iDocumentID, int iCompanyID, FileAccessType eAccessType, CancellationToken oCancellationToken)
    {
        var oParam = new DynamicParameters();
        oParam.Add("@DocumentID", iDocumentID);
        oParam.Add("@CompanyID", iCompanyID);
        oParam.Add("@AccessType", eAccessType.ToString());
        return oSqlHelper.ExecQuerySingleAsync<DocumentFileModel>("dbo.GetDocumentFileByID", oParam, oCancellationToken);
    }

    public Task<DeleteDocumentResultModel> DeleteDocumentByIDAsync(int iDocumentID, int iCompanyID, CancellationToken oCancellationToken)
    {
        var oParam = new DynamicParameters();
        oParam.Add("@DocumentID", iDocumentID);
        oParam.Add("@CompanyID", iCompanyID);
        return oSqlHelper.ExecQuerySingleAsync<DeleteDocumentResultModel>("dbo.DeleteDocumentByID", oParam, oCancellationToken);
    }

    public Task<DocumentNoteModel> InsertDocumentNoteAsync(int iDocumentID, int iCompanyID, string? strNoteText, decimal? decPositionTop, decimal? decPositionLeft, CancellationToken oCancellationToken)
    {
        var oParam = new DynamicParameters();
        oParam.Add("@DocumentID", iDocumentID);
        oParam.Add("@CompanyID", iCompanyID);
        oParam.Add("@NoteText", strNoteText);
        oParam.Add("@PositionTop", decPositionTop);
        oParam.Add("@PositionLeft", decPositionLeft);
        return oSqlHelper.ExecQuerySingleAsync<DocumentNoteModel>("dbo.InsertDocumentNote", oParam, oCancellationToken);
    }

    public Task<IReadOnlyList<DocumentNoteModel>> GetDocumentNotesByDocumentIDAsync(int iDocumentID, int iCompanyID, CancellationToken oCancellationToken)
    {
        var oParam = new DynamicParameters();
        oParam.Add("@DocumentID", iDocumentID);
        oParam.Add("@CompanyID", iCompanyID);
        return oSqlHelper.ExecQueryAsync<DocumentNoteModel>("dbo.GetDocumentNotesByDocumentID", oParam, oCancellationToken);
    }
}
