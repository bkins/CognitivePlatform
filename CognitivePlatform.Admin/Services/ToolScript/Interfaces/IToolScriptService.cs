namespace CognitivePlatform.Admin.Services.ToolScript.Interfaces;

public interface IToolScriptService
{
    IReadOnlyList<Models.ToolScript> GetTools();
}