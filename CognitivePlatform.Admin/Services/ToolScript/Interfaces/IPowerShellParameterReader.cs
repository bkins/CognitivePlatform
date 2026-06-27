using CognitivePlatform.Admin.Services.ToolScript.Models;

namespace CognitivePlatform.Admin.Services.ToolScript.Interfaces;

public interface IPowerShellParameterReader
{
    IReadOnlyList<ToolParameter> Read(string scriptPath);
}