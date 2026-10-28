using DocumentManagement.Web.Helper;
using Serilog.Context;

namespace DocumentManagement.Web.Middleware;

// Adds CompanyID and ErrorId to every log line of the request.
public class LogContextMiddleware
{
    private readonly RequestDelegate fnNext;

    public LogContextMiddleware(RequestDelegate fnNext)
    {
        this.fnNext = fnNext;
    }

    public async Task InvokeAsync(HttpContext oHttpContext, ICompanyContext oCompanyContext)
    {
        int iCompanyID = await oCompanyContext.GetCompanyIDAsync(oHttpContext.RequestAborted);
        using (LogContext.PushProperty("CompanyID", iCompanyID))
        using (LogContext.PushProperty("ErrorId", GlobalExceptionHandler.GetErrorId(oHttpContext)))
        {
            await fnNext(oHttpContext);
        }
    }
}
