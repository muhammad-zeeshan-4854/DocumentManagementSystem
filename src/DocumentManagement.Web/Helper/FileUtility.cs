using DocumentManagement.Web.Exceptions;
using DocumentManagement.Web.Models;

namespace DocumentManagement.Web.Helper;

// File locations always come from the database, never from the browser.
public interface IFileUtility
{
    Task<FileCheckResultModel> CheckFileAsync(IFormFile oFile, CancellationToken oCancellationToken);

    Task SaveFileAsync(string strFileBasePath, string strFilePath, string strStoredFileName, Stream oContent, CancellationToken oCancellationToken);

    Stream OpenFile(string strFileBasePath, string strFilePath, string strStoredFileName);

    string GetRootFolder(string strFileBasePath);
}

public class FileUtility : IFileUtility
{
    private const int iFileHeaderLength = 8;
    private const int iBufferSize = 81920;

    private readonly string strContentRootPath;
    private readonly IEnumerable<FileContentChecker> lstContentCheckers;
    private readonly ILogger<FileUtility> oLogger;

    public FileUtility(IWebHostEnvironment oEnvironment, IEnumerable<FileContentChecker> lstContentCheckers, ILogger<FileUtility> oLogger)
    {
        strContentRootPath = oEnvironment.ContentRootPath;
        this.lstContentCheckers = lstContentCheckers;
        this.oLogger = oLogger;
    }

    public string GetRootFolder(string strFileBasePath)
    {
        if (string.IsNullOrWhiteSpace(strFileBasePath))
        {
            throw new FileStorageException("FileBasePath in dbo.Parameters is empty.", "");
        }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(strContentRootPath, strFileBasePath)));
    }

    public async Task<FileCheckResultModel> CheckFileAsync(IFormFile oFile, CancellationToken oCancellationToken)
    {
        await using Stream oStream = oFile.OpenReadStream();

        var aryHeader = new byte[iFileHeaderLength];
        int iHeaderLength = await oStream.ReadAtLeastAsync(aryHeader, iFileHeaderLength, throwOnEndOfStream: false, oCancellationToken);
        oStream.Position = 0;

        string strExtension = Path.GetExtension(oFile.FileName);
        FileContentChecker? oChecker = lstContentCheckers.FirstOrDefault(oItem => oItem.CanCheck(strExtension));
        FileContentCheckResult oContent = oChecker is null
            ? FileContentCheckResult.Clean
            : await oChecker.CheckAsync(oStream, oCancellationToken);

        if (oContent.IsCorrupt || oContent.IsPasswordProtected || oContent.HasActiveContent)
        {
            oLogger.LogInformation("Upload {FileName} ({FileSizeBytes} bytes) checked by {CheckerName}: corrupt={IsCorrupt}, password={IsPasswordProtected}, activeContent={HasActiveContent}",
                oFile.FileName, oFile.Length, oChecker?.GetType().Name, oContent.IsCorrupt, oContent.IsPasswordProtected, oContent.HasActiveContent);
        }

        return new FileCheckResultModel
        {
            FileHeader = aryHeader[..iHeaderLength],
            IsCorrupt = oContent.IsCorrupt,
            IsPasswordProtected = oContent.IsPasswordProtected,
            HasActiveContent = oContent.HasActiveContent
        };
    }

    public async Task SaveFileAsync(string strFileBasePath, string strFilePath, string strStoredFileName, Stream oContent, CancellationToken oCancellationToken)
    {
        string strFullPath = GetFullPath(strFileBasePath, strFilePath, strStoredFileName);
        // Write to a temp name, then rename, so a half-written file is never served.
        string strTempPath = strFullPath + ".uploading";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(strFullPath)!);
            await using (var oFileStream = new FileStream(strTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, iBufferSize, useAsync: true))
            {
                await oContent.CopyToAsync(oFileStream, oCancellationToken);
            }
            File.Move(strTempPath, strFullPath);
            oLogger.LogDebug("Saved {StoredFileName} to {FullPath}", strStoredFileName, strFullPath);
        }
        catch (Exception ex)
        {
            DeleteTempFile(strTempPath);
            if (ex is OperationCanceledException)
            {
                throw;
            }
            throw new FileStorageException($"Could not save file to {strFullPath}.", strStoredFileName, ex);
        }
    }

    public Stream OpenFile(string strFileBasePath, string strFilePath, string strStoredFileName)
    {
        string strFullPath = GetFullPath(strFileBasePath, strFilePath, strStoredFileName);
        try
        {
            return new FileStream(strFullPath, FileMode.Open, FileAccess.Read, FileShare.Read, iBufferSize, useAsync: true);
        }
        catch (Exception ex)
        {
            throw new FileStorageException($"Could not open file {strFullPath}.", strStoredFileName, ex);
        }
    }

    private string GetFullPath(string strFileBasePath, string strFilePath, string strStoredFileName)
    {
        string strRootPath = GetRootFolder(strFileBasePath);

        if (string.IsNullOrWhiteSpace(strStoredFileName)
            || Path.GetFileName(strStoredFileName) != strStoredFileName
            || strStoredFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new FileStorageException("Stored file name is not a plain file name.", strStoredFileName);
        }

        if (string.IsNullOrWhiteSpace(strFilePath)
            || Path.IsPathRooted(strFilePath)
            || strFilePath.Split('/', '\\').Any(strPart => strPart is "" or "." or ".."))
        {
            throw new FileStorageException($"File path '{strFilePath}' is not a valid relative folder.", strStoredFileName);
        }

        string strFullPath = Path.GetFullPath(Path.Combine(strRootPath, strFilePath, strStoredFileName));
        if (!strFullPath.StartsWith(strRootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new FileStorageException($"File path '{strFilePath}' resolves outside the storage folder.", strStoredFileName);
        }
        return strFullPath;
    }

    private void DeleteTempFile(string strTempPath)
    {
        try
        {
            if (File.Exists(strTempPath))
            {
                File.Delete(strTempPath);
            }
        }
        catch (Exception ex)
        {
            oLogger.LogWarning(ex, "Could not remove partial file {TempPath}; delete it manually", strTempPath);
        }
    }
}
