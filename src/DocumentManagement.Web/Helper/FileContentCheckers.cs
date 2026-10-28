using System.IO.Compression;
using System.Text;

namespace DocumentManagement.Web.Helper;

public class FileContentCheckResult
{
    public static readonly FileContentCheckResult Clean = new();

    public bool IsCorrupt { get; init; }
    public bool IsPasswordProtected { get; init; }
    public bool HasActiveContent { get; init; }
}

public abstract class FileContentChecker
{
    protected const int iBlockSize = 81920;

    public abstract IReadOnlyCollection<string> FileExtensions { get; }

    public bool CanCheck(string strFileExtension) => FileExtensions.Contains(strFileExtension, StringComparer.OrdinalIgnoreCase);

    public abstract Task<FileContentCheckResult> CheckAsync(Stream oStream, CancellationToken oCancellationToken);

    protected static async Task<byte[]> ReadTailAsync(Stream oStream, int iLength, CancellationToken oCancellationToken)
    {
        int iTailLength = (int)Math.Min(iLength, oStream.Length);
        var aryTail = new byte[iTailLength];
        oStream.Seek(-iTailLength, SeekOrigin.End);
        await oStream.ReadExactlyAsync(aryTail, oCancellationToken);
        oStream.Position = 0;
        return aryTail;
    }

    protected static bool Contains(byte[] arySource, byte[] aryPattern) => arySource.AsSpan().IndexOf(aryPattern) >= 0;
}

public class PdfFileChecker : FileContentChecker
{
    // PDF keys that run code or carry other files.
    private static readonly string[] aryActiveContentKeys = { "/JavaScript", "/JS", "/Launch", "/EmbeddedFile", "/RichMedia", "/XFA" };
    private const string strEncryptKey = "/Encrypt";

    public override IReadOnlyCollection<string> FileExtensions { get; } = new[] { ".pdf" };

    public override async Task<FileContentCheckResult> CheckAsync(Stream oStream, CancellationToken oCancellationToken)
    {
        bool bHasActiveContent = false;
        bool bIsPasswordProtected = false;

        // Keep the end of the last block so a key split across two blocks is still found.
        int iOverlap = aryActiveContentKeys.Append(strEncryptKey).Max(strKey => strKey.Length) - 1;
        var aryBuffer = new byte[iBlockSize];
        string strCarry = "";
        int iRead;
        while ((iRead = await oStream.ReadAsync(aryBuffer, oCancellationToken)) > 0)
        {
            string strBlock = strCarry + Encoding.Latin1.GetString(aryBuffer, 0, iRead);
            bHasActiveContent |= aryActiveContentKeys.Any(strKey => strBlock.Contains(strKey, StringComparison.Ordinal));
            bIsPasswordProtected |= strBlock.Contains(strEncryptKey, StringComparison.Ordinal);
            strCarry = strBlock.Length > iOverlap ? strBlock[^iOverlap..] : strBlock;
        }

        // A complete PDF ends with %%EOF.
        byte[] aryTail = await ReadTailAsync(oStream, 1024, oCancellationToken);
        bool bIsCorrupt = !Contains(aryTail, "%%EOF"u8.ToArray());

        return new FileContentCheckResult
        {
            IsCorrupt = bIsCorrupt,
            IsPasswordProtected = bIsPasswordProtected,
            HasActiveContent = bHasActiveContent
        };
    }
}

public class OfficeFileChecker : FileContentChecker
{
    // Password protected .docx/.xlsx files are not zip files: Office wraps them in an OLE container
    // (starts with D0 CF 11 E0) holding an "EncryptedPackage" stream.
    private static readonly byte[] aryOleSignature = { 0xD0, 0xCF, 0x11, 0xE0 };
    private static readonly byte[] aryEncryptedPackage = Encoding.Unicode.GetBytes("EncryptedPackage");

    public override IReadOnlyCollection<string> FileExtensions { get; } = new[] { ".docx", ".xlsx" };

    public override async Task<FileContentCheckResult> CheckAsync(Stream oStream, CancellationToken oCancellationToken)
    {
        var aryHeader = new byte[aryOleSignature.Length];
        int iHeaderLength = await oStream.ReadAtLeastAsync(aryHeader, aryHeader.Length, throwOnEndOfStream: false, oCancellationToken);
        oStream.Position = 0;

        if (iHeaderLength == aryOleSignature.Length && aryHeader.AsSpan().SequenceEqual(aryOleSignature))
        {
            bool bIsEncrypted = await StreamContainsAsync(oStream, aryEncryptedPackage, oCancellationToken);
            return bIsEncrypted
                ? new FileContentCheckResult { IsPasswordProtected = true }
                : new FileContentCheckResult { IsCorrupt = true };  // an old binary .doc/.xls renamed to .docx/.xlsx
        }

        try
        {
            using var oZip = new ZipArchive(oStream, ZipArchiveMode.Read, leaveOpen: true);
            if (oZip.GetEntry("[Content_Types].xml") is null)
            {
                return new FileContentCheckResult { IsCorrupt = true };
            }
            return new FileContentCheckResult { HasActiveContent = HasActiveContent(oZip) };
        }
        catch (InvalidDataException)
        {
            return new FileContentCheckResult { IsCorrupt = true };
        }
        finally
        {
            oStream.Position = 0;
        }
    }

    private static bool HasActiveContent(ZipArchive oZip)
    {
        foreach (ZipArchiveEntry oEntry in oZip.Entries)
        {
            string strName = oEntry.FullName;
            if (strName.Contains("vbaProject.bin", StringComparison.OrdinalIgnoreCase)
                || strName.Contains("/activeX/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!strName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && !strName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var oReader = new StreamReader(oEntry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string strXml = oReader.ReadToEnd();
            if (strXml.Contains("DDEAUTO", StringComparison.OrdinalIgnoreCase)
                || strXml.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static async Task<bool> StreamContainsAsync(Stream oStream, byte[] aryPattern, CancellationToken oCancellationToken)
    {
        var aryBuffer = new byte[iBlockSize + aryPattern.Length];
        int iCarry = 0;
        int iRead;
        while ((iRead = await oStream.ReadAsync(aryBuffer.AsMemory(iCarry, iBlockSize), oCancellationToken)) > 0)
        {
            int iLength = iCarry + iRead;
            if (aryBuffer.AsSpan(0, iLength).IndexOf(aryPattern) >= 0)
            {
                oStream.Position = 0;
                return true;
            }
            iCarry = Math.Min(aryPattern.Length - 1, iLength);
            Array.Copy(aryBuffer, iLength - iCarry, aryBuffer, 0, iCarry);
        }
        oStream.Position = 0;
        return false;
    }
}

public class ImageFileChecker : FileContentChecker
{
    private static readonly byte[] aryPngEnd = "IEND"u8.ToArray();
    private static readonly byte[] aryJpegEnd = { 0xFF, 0xD9 };

    public override IReadOnlyCollection<string> FileExtensions { get; } = new[] { ".png", ".jpg", ".jpeg" };

    public override async Task<FileContentCheckResult> CheckAsync(Stream oStream, CancellationToken oCancellationToken)
    {
        byte[] aryTail = await ReadTailAsync(oStream, 32, oCancellationToken);
        bool bIsPng = oStream.Length >= 8 && await StartsWithPngSignatureAsync(oStream, oCancellationToken);
        bool bIsCorrupt = bIsPng ? !Contains(aryTail, aryPngEnd) : !Contains(aryTail, aryJpegEnd);
        return bIsCorrupt ? new FileContentCheckResult { IsCorrupt = true } : FileContentCheckResult.Clean;
    }

    private static async Task<bool> StartsWithPngSignatureAsync(Stream oStream, CancellationToken oCancellationToken)
    {
        var aryHeader = new byte[4];
        await oStream.ReadExactlyAsync(aryHeader, oCancellationToken);
        oStream.Position = 0;
        return aryHeader[0] == 0x89 && aryHeader[1] == 0x50 && aryHeader[2] == 0x4E && aryHeader[3] == 0x47;
    }
}

public class TextFileChecker : FileContentChecker
{
    public override IReadOnlyCollection<string> FileExtensions { get; } = new[] { ".txt" };

    // A NUL byte means a binary file renamed to .txt (UTF-16 starts with a BOM and is skipped).
    public override async Task<FileContentCheckResult> CheckAsync(Stream oStream, CancellationToken oCancellationToken)
    {
        var aryBuffer = new byte[iBlockSize];
        int iRead = await oStream.ReadAsync(aryBuffer, oCancellationToken);
        bool bIsUtf16 = iRead >= 2 && ((aryBuffer[0] == 0xFF && aryBuffer[1] == 0xFE) || (aryBuffer[0] == 0xFE && aryBuffer[1] == 0xFF));
        if (bIsUtf16)
        {
            oStream.Position = 0;
            return FileContentCheckResult.Clean;
        }

        do
        {
            if (Array.IndexOf(aryBuffer, (byte)0, 0, iRead) >= 0)
            {
                oStream.Position = 0;
                return new FileContentCheckResult { IsCorrupt = true };
            }
        }
        while ((iRead = await oStream.ReadAsync(aryBuffer, oCancellationToken)) > 0);

        oStream.Position = 0;
        return FileContentCheckResult.Clean;
    }
}
