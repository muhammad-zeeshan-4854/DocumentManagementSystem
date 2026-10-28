using DocumentManagement.Web.Helper;
using Microsoft.AspNetCore.Mvc;

namespace DocumentManagement.Web.Controllers;

// Always back to the list: the open page may belong to the old company.
public class CompanyController : Controller
{
    private readonly ILogger<CompanyController> oLogger;

    public CompanyController(ILogger<CompanyController> oLogger)
    {
        this.oLogger = oLogger;
    }

    [HttpPost]
    public IActionResult Switch([FromForm(Name = "companyID")] int? iCompanyID)
    {
        if (iCompanyID is not null)
        {
            Response.Cookies.Append(CookieCompanyContext.CookieName, iCompanyID.Value.ToString(), new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps
            });
            oLogger.LogInformation("Switched to company {NewCompanyID}", iCompanyID.Value);
        }

        return RedirectToAction("Index", "Documents");
    }
}
