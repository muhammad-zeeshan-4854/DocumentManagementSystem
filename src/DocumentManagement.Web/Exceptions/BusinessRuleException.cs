namespace DocumentManagement.Web.Exceptions;

// Business error thrown by an SP (50400 / 50404 / 50409). The message is safe to show.
public class BusinessRuleException : Exception
{
    public const int ValidationErrorNumber = 50400;
    public const int NotFoundErrorNumber = 50404;
    public const int ConflictErrorNumber = 50409;

    public int SqlErrorNumber { get; }

    public int StatusCode { get; }

    public BusinessRuleException(int iSqlErrorNumber, string strMessage, Exception? oInnerException = null)
        : base(strMessage, oInnerException)
    {
        SqlErrorNumber = iSqlErrorNumber;
        StatusCode = iSqlErrorNumber switch
        {
            NotFoundErrorNumber => StatusCodes.Status404NotFound,
            ConflictErrorNumber => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
    }

    public static bool IsBusinessErrorNumber(int iSqlErrorNumber) =>
        iSqlErrorNumber is ValidationErrorNumber or NotFoundErrorNumber or ConflictErrorNumber;
}
