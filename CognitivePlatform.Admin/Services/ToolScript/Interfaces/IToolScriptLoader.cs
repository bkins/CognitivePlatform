namespace CognitivePlatform.Admin.Services.ToolScript.Interfaces;

public interface IToolScriptLoader
{
    Models.ToolScript Load(string scriptPath);
}