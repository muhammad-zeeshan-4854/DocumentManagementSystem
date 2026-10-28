using DocumentManagement.Web.DataServices;
using DocumentManagement.Web.Helper;
using DocumentManagement.Web.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace DocumentManagement.Web.Services;

public interface IParameterService
{
    Task<ParametersModel> GetParametersAsync(CancellationToken oCancellationToken);
}

public class ParameterService : IParameterService
{
    private const string strCacheKey = "dbo.Parameters";
    private const int iFallbackCacheSeconds = 10;

    // Used when the database can't be reached, so pages can still render.
    private static readonly ParametersModel oFallbackParameters = new()
    {
        SystemName = "Document Management",
        MaxUploadSizeBytes = 25 * 1024 * 1024,
        DefaultPageSize = 10,
        MaxPageSize = 100,
        MaxTitleLength = 200,
        MaxNoteLength = 1000,
        DefaultCompanyID = 0  // no company: the SPs refuse it, which is right while the database is unknown
    };

    private readonly IParameterDataService oDataService;
    private readonly IMemoryCache oCache;
    private readonly ParameterSettings oSettings;
    private readonly ILogger<ParameterService> oLogger;

    public ParameterService(IParameterDataService oDataService, IMemoryCache oCache, IOptions<ParameterSettings> oSettings,
        ILogger<ParameterService> oLogger)
    {
        this.oDataService = oDataService;
        this.oCache = oCache;
        this.oSettings = oSettings.Value;
        this.oLogger = oLogger;
    }

    public async Task<ParametersModel> GetParametersAsync(CancellationToken oCancellationToken)
    {
        if (oCache.TryGetValue(strCacheKey, out ParametersModel? oCached) && oCached is not null)
        {
            return oCached;
        }

        try
        {
            ParametersModel oParameters = await oDataService.GetParametersAsync(oCancellationToken);
            oCache.Set(strCacheKey, oParameters, TimeSpan.FromSeconds(oSettings.CacheSeconds));
            return oParameters;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Kept briefly so every request doesn't wait for a database timeout.
            oLogger.LogWarning("Could not read dbo.Parameters, using built-in defaults for {FallbackSeconds} s: {ErrorMessage}",
                iFallbackCacheSeconds, ex.Message);
            oCache.Set(strCacheKey, oFallbackParameters, TimeSpan.FromSeconds(iFallbackCacheSeconds));
            return oFallbackParameters;
        }
    }
}
