using CognitivePlatform.Api.Insights.Models;

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

    /// <summary>
    /// Insights emitted by the Insight Engine on this turn. <see cref="Message"/>
    /// is the woven version (insights folded into prose) when this list is non-empty,
    /// or the raw execution result otherwise. Clients that only render
    /// <see cref="Message"/> see the same UX as before; clients that want to display
    /// insights distinctly can read this list directly.
    /// </summary>
    public IReadOnlyList<Insight> Insights { get; set; } = Array.Empty<Insight>();
}
