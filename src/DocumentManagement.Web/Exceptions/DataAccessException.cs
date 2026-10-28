namespace DocumentManagement.Web.Exceptions;

public class DataAccessException : Exception
{
    public string ProcedureName { get; }

    public string Parameters { get; }

    public long ElapsedMilliseconds { get; }

    public int? SqlErrorNumber { get; }

    public DataAccessException(string strProcedureName, string strParameters, long lElapsedMilliseconds, int? iSqlErrorNumber, Exception oInnerException)
        : base($"Stored procedure {strProcedureName} failed after {lElapsedMilliseconds} ms" +
               $"{(iSqlErrorNumber.HasValue ? $" with SQL error {iSqlErrorNumber}" : "")}. " +
               $"Parameters: {strParameters}. Error: {oInnerException.Message}", oInnerException)
    {
        ProcedureName = strProcedureName;
        Parameters = strParameters;
        ElapsedMilliseconds = lElapsedMilliseconds;
        SqlErrorNumber = iSqlErrorNumber;
    }
}
