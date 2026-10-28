using Dapper;
using DocumentManagement.Web.Helper;
using DocumentManagement.Web.Models;

namespace DocumentManagement.Web.DataServices;

public interface ICompanyDataService
{
    Task<IReadOnlyList<CompanyModel>> GetCompanyListAsync(CancellationToken oCancellationToken);
}

public class CompanyDataService : ICompanyDataService
{
    private readonly ISqlHelper oSqlHelper;

    public CompanyDataService(ISqlHelper oSqlHelper)
    {
        this.oSqlHelper = oSqlHelper;
    }

    public Task<IReadOnlyList<CompanyModel>> GetCompanyListAsync(CancellationToken oCancellationToken)
    {
        return oSqlHelper.ExecQueryAsync<CompanyModel>("dbo.GetCompanyList", new DynamicParameters(), oCancellationToken);
    }
}
