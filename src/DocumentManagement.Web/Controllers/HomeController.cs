using DocumentManagement.Web.Middleware;
using DocumentManagement.Web.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace DocumentManagement.Web.Controllers;

public class HomeController : Controller
{
    private readonly IWebHostEnvironment oEnvironment;

    public HomeController(IWebHostEnvironment oEnvironment)
    {
        this.oEnvironment = oEnvironment;
    }

    public IActionResult Index() => RedirectToAction("Index", "Documents");

    [IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error([FromQuery(Name = "statusCode")] int? iStatusCode)
    {
        Exception? oException = HttpContext.Features.Get<IExceptionHandlerFeature>()?.Error;
        int iFinalStatusCode = iStatusCode ?? (oException is null ? Response.StatusCode : GlobalExceptionHandler.GetStatusCode(oException));
        if (iFinalStatusCode < 400)
        {
            iFinalStatusCode = StatusCodes.Status500InternalServerError;
        }
        string strErrorId = GlobalExceptionHandler.GetErrorId(HttpContext);
        string strMessage = oException is null && iFinalStatusCode == StatusCodes.Status404NotFound
            ? "The page you asked for does not exist."
            : GlobalExceptionHandler.GetUserMessage(oException, iFinalStatusCode, strErrorId);

        Response.StatusCode = iFinalStatusCode;

        if (GlobalExceptionHandler.IsJsonRequest(Request))
        {
            var oProblem = new ProblemDetails
            {
                Status = iFinalStatusCode,
                Title = GlobalExceptionHandler.GetTitle(iFinalStatusCode),
                Detail = strMessage
            };
            oProblem.Extensions["errorId"] = strErrorId;
            return new ObjectResult(oProblem) { StatusCode = iFinalStatusCode, ContentTypes = { "application/problem+json" } };
        }

        return View(new ErrorViewModel
        {
            StatusCode = iFinalStatusCode,
            Title = GlobalExceptionHandler.GetTitle(iFinalStatusCode),
            Message = strMessage,
            ErrorId = strErrorId,
            DeveloperDetails = oEnvironment.IsDevelopment() && iFinalStatusCode >= 500 ? oException?.ToString() : null
        });
    }
}
