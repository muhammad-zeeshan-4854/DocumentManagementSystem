using System.Diagnostics;
using DocumentManagement.Web.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocumentManagement.Web.Middleware;

// Logs each unhandled exception once. JSON requests get ProblemDetails, pages go to /Home/Error.
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> oLogger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> oLogger)
    {
        this.oLogger = oLogger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext oHttpContext, Exception oException, CancellationToken oCancellationToken)
    {
        int iStatusCode = GetStatusCode(oException);
        string strErrorId = GetErrorId(oHttpContext);
        var oRequest = oHttpContext.Request;
        // Request.Path already points to the error page here; this is the URL that actually failed.
        string strPath = oHttpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? oRequest.Path.Value ?? "";

        if (oException is OperationCanceledException)
        {
            oLogger.LogInformation("Request {Method} {Path} was cancelled by the client - ErrorId {ErrorId}",
                oRequest.Method, strPath, strErrorId);
        }
        else if (iStatusCode >= StatusCodes.Status500InternalServerError)
        {
            oLogger.LogError(oException, "Unhandled {ExceptionType} for {Method} {Path}{QueryString} -> {StatusCode} - ErrorId {ErrorId} - {ErrorMessage}",
                oException.GetType().Name, oRequest.Method, strPath, oRequest.QueryString, iStatusCode, strErrorId, oException.Message);
        }
        else
        {
            oLogger.LogWarning("{ExceptionType} for {Method} {Path}{QueryString} -> {StatusCode}: {ErrorMessage} - ErrorId {ErrorId}",
                oException.GetType().Name, oRequest.Method, strPath, oRequest.QueryString, iStatusCode, oException.Message, strErrorId);
        }

        if (!IsJsonRequest(oRequest) || oHttpContext.Response.HasStarted)
        {
            return false;
        }

        var oProblem = new ProblemDetails
        {
            Status = iStatusCode,
            Title = GetTitle(iStatusCode),
            Detail = GetUserMessage(oException, iStatusCode, strErrorId),
            Instance = strPath
        };
        oProblem.Extensions["errorId"] = strErrorId;

        oHttpContext.Response.StatusCode = iStatusCode;
        await oHttpContext.Response.WriteAsJsonAsync(oProblem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", oCancellationToken);
        return true;
    }

    public static int GetStatusCode(Exception oException) => oException switch
    {
        BusinessRuleException oBusiness => oBusiness.StatusCode,
        BadHttpRequestException oBadRequest => oBadRequest.StatusCode,
        OperationCanceledException => 499,
        _ => StatusCodes.Status500InternalServerError
    };

    public static string GetErrorId(HttpContext oHttpContext) => Activity.Current?.TraceId.ToString() ?? oHttpContext.TraceIdentifier;

    public static string GetUserMessage(Exception? oException, int iStatusCode, string strErrorId) => oException switch
    {
        BusinessRuleException oBusiness => oBusiness.Message,
        BadHttpRequestException oBadRequest when iStatusCode == StatusCodes.Status413PayloadTooLarge =>
            oBadRequest.Message.StartsWith("The upload is too large", StringComparison.Ordinal) ? oBadRequest.Message : "The upload is too large.",
        _ when iStatusCode < 500 => GetTitle(iStatusCode),
        _ => $"Something went wrong on our side. Please try again, and quote error id {strErrorId} if it keeps happening."
    };

    public static string GetTitle(int iStatusCode) => iStatusCode switch
    {
        StatusCodes.Status400BadRequest => "The request could not be processed",
        StatusCodes.Status404NotFound => "Not found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status413PayloadTooLarge => "Upload too large",
        _ when iStatusCode >= 500 => "Unexpected error",
        _ => "Request failed"
    };

    public static bool IsJsonRequest(HttpRequest oRequest)
    {
        return oRequest.Headers.Accept.Any(strAccept => strAccept?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            || oRequest.Headers.XRequestedWith == "XMLHttpRequest"
            || oRequest.HasJsonContentType();
    }
}
