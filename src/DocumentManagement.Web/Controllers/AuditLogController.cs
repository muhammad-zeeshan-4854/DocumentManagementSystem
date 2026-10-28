using DocumentManagement.Web.DataServices;
using DocumentManagement.Web.Exceptions;
using DocumentManagement.Web.Helper;
using DocumentManagement.Web.Models;
using DocumentManagement.Web.Services;
using DocumentManagement.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DocumentManagement.Web.Controllers;

public class AuditLogController : Controller
{
    private readonly IAuditLogDataService oAuditLogDataService;
    private readonly ICompanyContext oCompanyContext;
    private readonly IParameterService oParameterService;

    public AuditLogController(IAuditLogDataService oAuditLogDataService, ICompanyContext oCompanyContext, IParameterService oParameterService)
    {
        this.oAuditLogDataService = oAuditLogDataService;
        this.oCompanyContext = oCompanyContext;
        this.oParameterService = oParameterService;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery(Name = "page")] int iPageNumber = 1, CancellationToken oCancellationToken = default)
    {
        var oModel = new AuditLogListViewModel();
        try
        {
            int iCompanyID = await oCompanyContext.GetCompanyIDAsync(oCancellationToken);
            ParametersModel oParameters = await oParameterService.GetParametersAsync(oCancellationToken);
            oModel.AuditLogs = await oAuditLogDataService.GetAuditLogListAsync(iCompanyID, iPageNumber, oParameters.DefaultPageSize, oCancellationToken);
        }
        catch (BusinessRuleException ex)
        {
            oModel.ErrorMessage = ex.Message;
        }
        return View(oModel);
    }
}
