using DocumentManagement.Web.Models;
using DocumentManagement.Web.Services;
using DocumentManagement.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace DocumentManagement.Web.Controllers;

[ApiController]
[Route("documents/{documentID:int}/notes")]
public class DocumentNotesController : ControllerBase
{
    private readonly IDocumentService oDocumentService;

    public DocumentNotesController(IDocumentService oDocumentService)
    {
        this.oDocumentService = oDocumentService;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DocumentNoteModel>>> GetNotes([FromRoute(Name = "documentID")] int iDocumentID,
        CancellationToken oCancellationToken)
    {
        return Ok(await oDocumentService.GetNotesAsync(iDocumentID, oCancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<DocumentNoteModel>> AddNote([FromRoute(Name = "documentID")] int iDocumentID,
        [FromBody] AddNoteRequestModel oRequest, CancellationToken oCancellationToken)
    {
        DocumentNoteModel oNote = await oDocumentService.AddNoteAsync(iDocumentID, oRequest.NoteText, oRequest.PositionTop, oRequest.PositionLeft, oCancellationToken);
        return Created($"/documents/{iDocumentID}/notes", oNote);
    }
}
