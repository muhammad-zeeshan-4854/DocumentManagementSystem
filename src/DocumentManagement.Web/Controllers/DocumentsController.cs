using DocumentManagement.Web.Exceptions;
using DocumentManagement.Web.Helper;
using DocumentManagement.Web.Models;
using DocumentManagement.Web.Services;
using DocumentManagement.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DocumentManagement.Web.Controllers;

public class DocumentsController : Controller
{
    private const string strUploadErrorKey = "UploadErrorMessage";
    private const string strUploadTitleKey = "UploadTitle";

    private readonly IDocumentService oDocumentService;
    private readonly IParameterService oParameterService;
    private readonly IWordPreviewBuilder oWordPreviewBuilder;

    public DocumentsController(IDocumentService oDocumentService, IParameterService oParameterService, IWordPreviewBuilder oWordPreviewBuilder)
    {
        this.oDocumentService = oDocumentService;
        this.oParameterService = oParameterService;
        this.oWordPreviewBuilder = oWordPreviewBuilder;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery(Name = "search")] string? strSearch, [FromQuery(Name = "page")] int iPageNumber = 1,
        CancellationToken oCancellationToken = default)
    {
        var oModel = new DocumentListViewModel { Search = strSearch };
        try
        {
            ParametersModel oParameters = await oParameterService.GetParametersAsync(oCancellationToken);
            oModel.Documents = await oDocumentService.GetDocumentListAsync(strSearch, iPageNumber, oParameters.DefaultPageSize, oCancellationToken);
        }
        catch (BusinessRuleException ex)
        {
            oModel.ErrorMessage = ex.Message;
        }
        return View(oModel);
    }

    [HttpGet]
    public async Task<IActionResult> Upload(CancellationToken oCancellationToken)
    {
        var oModel = new UploadDocumentViewModel
        {
            Title = TempData[strUploadTitleKey] as string,
            AllowedFileTypes = await oDocumentService.GetAllowedFileTypeListAsync(oCancellationToken)
        };
        if (TempData[strUploadErrorKey] is string strUploadError)
        {
            ModelState.AddModelError(string.Empty, strUploadError);
        }
        return View(oModel);
    }

    // Always redirect, so the Back button never asks to resubmit the form.
    [HttpPost]
    public async Task<IActionResult> Upload(UploadDocumentViewModel oModel, CancellationToken oCancellationToken)
    {
        string strUploadError = ModelState.IsValid ? "" : "The upload could not be read. Please choose the file and try again.";
        if (ModelState.IsValid)
        {
            try
            {
                int iDocumentID = await oDocumentService.UploadDocumentAsync(oModel.Title, oModel.File, oCancellationToken);
                TempData["SuccessMessage"] = "Document uploaded.";
                return RedirectToAction(nameof(Details), new { id = iDocumentID });
            }
            catch (BusinessRuleException ex)
            {
                strUploadError = ex.Message;
            }
        }

        // Keep the title for the next try.
        TempData[strUploadErrorKey] = strUploadError;
        TempData[strUploadTitleKey] = oModel.Title;
        return RedirectToAction(nameof(Upload));
    }

    [HttpGet]
    public async Task<IActionResult> Details([FromRoute(Name = "id")] int iDocumentID, CancellationToken oCancellationToken)
    {
        try
        {
            DocumentDetailViewModel oModel = await oDocumentService.GetDocumentDetailAsync(iDocumentID, oCancellationToken);
            return View(oModel);
        }
        catch (BusinessRuleException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            // e.g. Back after a delete or a company switch.
            TempData["ErrorMessage"] = "That document is no longer available. It has been deleted, or it belongs to another company.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> ViewFile([FromRoute(Name = "id")] int iDocumentID, CancellationToken oCancellationToken)
    {
        FileDownloadModel oFile = await oDocumentService.GetDocumentFileAsync(iDocumentID, FileAccessType.View, oCancellationToken);

        // Browsers can't show .docx, so send an HTML copy.
        if (oFile.ContentType == WordPreviewBuilder.WordContentType)
        {
            string strHtml;
            await using (oFile.Content)
            {
                strHtml = oWordPreviewBuilder.BuildHtml(oFile.Content, oFile.OriginalFileName);
            }
            Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src data:; style-src 'unsafe-inline'";
            Response.Headers.CacheControl = "no-store, private";
            return Content(strHtml, "text/html; charset=utf-8");
        }

        // No sandbox for PDF: the browser's PDF viewer doesn't load inside one.
        if (oFile.ContentType != "application/pdf")
        {
            Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'; style-src 'unsafe-inline'";
        }
        return SendFile(oFile, "inline");
    }

    [HttpGet]
    public async Task<IActionResult> Download([FromRoute(Name = "id")] int iDocumentID, CancellationToken oCancellationToken)
    {
        FileDownloadModel oFile = await oDocumentService.GetDocumentFileAsync(iDocumentID, FileAccessType.Download, oCancellationToken);
        return SendFile(oFile, "attachment");
    }

    [HttpPost]
    public async Task<IActionResult> Delete([FromRoute(Name = "id")] int iDocumentID, CancellationToken oCancellationToken)
    {
        try
        {
            await oDocumentService.DeleteDocumentAsync(iDocumentID, oCancellationToken);
            TempData["SuccessMessage"] = "Document deleted.";
        }
        catch (BusinessRuleException ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    private FileStreamResult SendFile(FileDownloadModel oFile, string strDisposition)
    {
        var oContentDisposition = new ContentDispositionHeaderValue(strDisposition);
        oContentDisposition.SetHttpFileName(oFile.OriginalFileName);
        Response.Headers.ContentDisposition = oContentDisposition.ToString();
        Response.Headers.CacheControl = "no-store, private";

        return File(oFile.Content, oFile.ContentType, enableRangeProcessing: true);
    }
}
