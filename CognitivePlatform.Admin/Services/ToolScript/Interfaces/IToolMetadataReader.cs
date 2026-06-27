using CognitivePlatform.Admin.Services.ToolScript.Helpers;
using CognitivePlatform.Admin.Services.ToolScript.Models;

namespace CognitivePlatform.Admin.Services.ToolScript.Interfaces;

public interface IToolMetadataReader
{
    ToolMetadataReadResult Read(string scriptPath);
}