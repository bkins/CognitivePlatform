namespace CognitivePlatform.Api.Contracts;

public class ConverseResponse
{
    public string? InterpreterDebugOutput { get; set; }
    public string? SelectedAction         { get; set; }
    public string? ExecutionResult        { get; set; }
    public string? Message                { get; set; }
    public string? Debug                  { get; set; }
    public bool    Success                { get; set; } = true;
    public bool    WasFastPath            { get; set; }
    
    
}
