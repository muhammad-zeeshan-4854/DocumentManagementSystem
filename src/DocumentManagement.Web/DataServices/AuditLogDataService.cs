using Dapper;
using DocumentManagement.Web.Helper;
using DocumentManagement.Web.Models;

namespace DocumentManagement.Web.DataServices;

public interface IAuditLogDataService
{
    Task<PagedListModel<AuditLogModel>> GetAuditLogListAsync(int iCompanyID, int iPageNumber, int iPageSize, CancellationToken oCancellationToken);
}

public class AuditLogDataService : IAuditLogDataService
{
    private readonly ISqlHelper oSqlHelper;

    public AuditLogDataService(ISqlHelper oSqlHelper)
    {
        this.oSqlHelper = oSqlHelper;
    }

    public async Task<PagedListModel<AuditLogModel>> GetAuditLogListAsync(int iCompanyID, int iPageNumber, int iPageSize, CancellationToken oCancellationToken)
    {
        var oParam = new DynamicParameters();
        oParam.Add("@CompanyID", iCompanyID);
        oParam.Add("@PageNumber", iPageNumber);
        oParam.Add("@PageSize", iPageSize);

        var lstAuditLogs = await oSqlHelper.ExecQueryAsync<AuditLogModel>("dbo.GetAuditLogList", oParam, oCancellationToken);
        return new PagedListModel<AuditLogModel>
        {
            Items = lstAuditLogs,
            TotalCount = lstAuditLogs.Count > 0 ? lstAuditLogs[0].TotalCount : 0,
            PageNumber = iPageNumber,
            PageSize = iPageSize
        };
    }
}
