using DocumentManagement.Web.Services;

namespace DocumentManagement.Web.Helper;

public interface ICompanyContext
{
    Task<int> GetCompanyIDAsync(CancellationToken oCancellationToken);
}

// Company picked in the navbar (cookie), otherwise dbo.Parameters.DefaultCompanyID.
public class CookieCompanyContext : ICompanyContext
{
    public const string CookieName = "CompanyID";

    private readonly IHttpContextAccessor oHttpContextAccessor;
    private readonly IParameterService oParameterService;
    private int? iCompanyID;

    public CookieCompanyContext(IHttpContextAccessor oHttpContextAccessor, IParameterService oParameterService)
    {
        this.oHttpContextAccessor = oHttpContextAccessor;
        this.oParameterService = oParameterService;
    }

    public async Task<int> GetCompanyIDAsync(CancellationToken oCancellationToken)
    {
        if (iCompanyID is null)
        {
            string? strCookieValue = oHttpContextAccessor.HttpContext?.Request.Cookies[CookieName];
            iCompanyID = int.TryParse(strCookieValue, out int iCookieCompanyID)
                ? iCookieCompanyID
                : (await oParameterService.GetParametersAsync(oCancellationToken)).DefaultCompanyID;
        }
        return iCompanyID.Value;
    }
}
