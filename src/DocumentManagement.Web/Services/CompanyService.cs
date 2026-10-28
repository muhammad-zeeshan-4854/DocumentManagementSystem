using DocumentManagement.Web.DataServices;
using DocumentManagement.Web.Helper;
using DocumentManagement.Web.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DocumentManagement.Web.Services;

public interface ICompanyService
{
    Task<IReadOnlyList<CompanyModel>> GetCompanyListAsync(CancellationToken oCancellationToken);
}

public class CompanyService : ICompanyService
{
    private const string strCacheKey = "dbo.Company";

    private readonly ICompanyDataService oDataService;
    private readonly IMemoryCache oCache;
    private readonly ParameterSettings oSettings;
    private readonly ILogger<CompanyService> oLogger;

    public CompanyService(ICompanyDataService oDataService, IMemoryCache oCache, IOptions<ParameterSettings> oSettings,
        ILogger<CompanyService> oLogger)
    {
        this.oDataService = oDataService;
        this.oCache = oCache;
        this.oSettings = oSettings.Value;
        this.oLogger = oLogger;
    }

    public async Task<IReadOnlyList<CompanyModel>> GetCompanyListAsync(CancellationToken oCancellationToken)
    {
        if (oCache.TryGetValue(strCacheKey, out IReadOnlyList<CompanyModel>? lstCached) && lstCached is not null)
        {
            return lstCached;
        }

        try
        {
            IReadOnlyList<CompanyModel> lstCompanies = await oDataService.GetCompanyListAsync(oCancellationToken);
            oCache.Set(strCacheKey, lstCompanies, TimeSpan.FromSeconds(oSettings.CacheSeconds));
            return lstCompanies;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            oLogger.LogWarning("Could not read the company list: {ErrorMessage}", ex.Message);
            return [];
        }
    }
}
