using System.Globalization;

namespace DocumentManagement.Web.Helper;

public static class FormatHelper
{
    public static string FileSize(long lBytes)
    {
        if (lBytes < 1024)
        {
            return $"{lBytes} B";
        }
        if (lBytes < 1024 * 1024)
        {
            return (lBytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture) + " KB";
        }
        return (lBytes / (1024d * 1024d)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }

    public static string UtcDate(DateTime dtUtc) => dtUtc.ToString("dd MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";

    public static string AuditAction(string strActionType) => strActionType switch
    {
        "DocumentUploaded" => "Uploaded",
        "UploadFailed" => "Upload failed",
        "DocumentViewed" => "Viewed",
        "DocumentDownloaded" => "Downloaded",
        "DocumentDeleted" => "Deleted",
        "NoteAdded" => "Note added",
        _ => strActionType
    };

    public static string IsoDate(DateTime dtUtc) => dtUtc.ToString("o", CultureInfo.InvariantCulture);
}
