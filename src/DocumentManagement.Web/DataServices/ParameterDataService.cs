using Dapper;
using DocumentManagement.Web.Helper;
using DocumentManagement.Web.Models;

namespace DocumentManagement.Web.DataServices;

public interface IParameterDataService
{
    Task<ParametersModel> GetParametersAsync(CancellationToken oCancellationToken);
}

public class ParameterDataService : IParameterDataService
{
    private readonly ISqlHelper oSqlHelper;

    public ParameterDataService(ISqlHelper oSqlHelper)
    {
        this.oSqlHelper = oSqlHelper;
    }

    public Task<ParametersModel> GetParametersAsync(CancellationToken oCancellationToken)
    {
        return oSqlHelper.ExecQuerySingleAsync<ParametersModel>("dbo.GetParameters", new DynamicParameters(), oCancellationToken);
    }
}
