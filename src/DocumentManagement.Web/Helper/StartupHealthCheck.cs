using System.Diagnostics;
using DocumentManagement.Web.DataServices;

namespace DocumentManagement.Web.Helper;

// Logs the database and folder status at startup. Never stops the site.
public static class StartupHealthCheck
{
    public static async Task RunAsync(IServiceProvider oServices, ILogger oLogger)
    {
        using var oScope = oServices.CreateScope();

        var oStopwatch = Stopwatch.StartNew();
        try
        {
            var oDataService = oScope.ServiceProvider.GetRequiredService<IDocumentDataService>();
            var lstFileTypes = await oDataService.GetAllowedFileTypeListAsync(CancellationToken.None);
            oLogger.LogInformation("Database check passed in {ElapsedMs} ms. Allowed file types: {FileTypes}",
                oStopwatch.ElapsedMilliseconds, string.Join(", ", lstFileTypes.Select(oType => oType.FileExtension)));
        }
        catch (Exception ex)
        {
            oLogger.LogError(ex, "Database check failed after {ElapsedMs} ms. Pages will show an error until the database is reachable. {ErrorMessage}",
                oStopwatch.ElapsedMilliseconds, ex.Message);
        }

        string strRootPath = "";
        try
        {
            var oParameters = await oScope.ServiceProvider.GetRequiredService<IParameterDataService>().GetParametersAsync(CancellationToken.None);
            strRootPath = oScope.ServiceProvider.GetRequiredService<IFileUtility>().GetRootFolder(oParameters.FileBasePath);
            Directory.CreateDirectory(strRootPath);

            string strTestFile = Path.Combine(strRootPath, $".write-check-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(strTestFile, "ok");
            File.Delete(strTestFile);
            oLogger.LogInformation("File storage check passed. Documents are stored in {StorageRoot} (dbo.Parameters.FileBasePath)", strRootPath);
        }
        catch (Exception ex)
        {
            oLogger.LogError(ex, "File storage check failed for {StorageRoot}. Uploads and downloads will fail. {ErrorMessage}", strRootPath, ex.Message);
        }
    }
}
