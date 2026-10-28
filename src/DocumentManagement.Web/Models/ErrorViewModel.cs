namespace DocumentManagement.Web.Models;

public class ErrorViewModel
{
    public int StatusCode { get; init; }
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string ErrorId { get; init; } = "";

    public string? DeveloperDetails { get; init; }
}
