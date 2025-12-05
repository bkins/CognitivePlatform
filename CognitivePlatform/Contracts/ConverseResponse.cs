namespace CognitivePlatform.Api.Controllers;

public class ConverseResponse
{
    public string? InterpreterDebugOutput { get; set; }
    public string? SelectedAction         { get; set; }
    public string? ExecutionResult        { get; set; }
    public string? Message                { get; set; }
    public string? Debug                  { get; set; }
}
