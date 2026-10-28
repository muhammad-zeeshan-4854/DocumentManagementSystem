namespace DocumentManagement.Web.Exceptions;

public class FileStorageException : Exception
{
    public string StoredFileName { get; }

    public FileStorageException(string strMessage, string strStoredFileName, Exception? oInnerException = null)
        : base($"{strMessage} Stored file: {strStoredFileName}.", oInnerException)
    {
        StoredFileName = strStoredFileName;
    }
}
