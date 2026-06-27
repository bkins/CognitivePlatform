using CognitivePlatform.Admin.Services.ToolScript.Interfaces;
using CognitivePlatform.Admin.Services.ToolScript.Models;
using CP.Client.Core.Avails;

namespace CognitivePlatform.Admin.Services.ToolScript.Helpers;

public sealed class ToolScriptLoader : IToolScriptLoader
{
    private readonly IToolMetadataReader        _metadataReader;
    private readonly IPowerShellParameterReader _parameterReader;

    public ToolScriptLoader( IToolMetadataReader        metadataReader
                           , IPowerShellParameterReader parameterReader )
    {
        _metadataReader  = metadataReader;
        _parameterReader = parameterReader;
    }

    public Models.ToolScript Load( string scriptPath )
    {
        var metadataResult = _metadataReader.Read(scriptPath);

        if (metadataResult.Success.Not())
        {
            return new Models.ToolScript
                   {
                           ScriptPath    = scriptPath
                         , Status        = ToolLoadStatus.MissingMetadata
                         , StatusMessage = metadataResult.ErrorMessage
                   };
        }
        
        try
        {
            return new Models.ToolScript
                   {
                           ScriptPath    = scriptPath
                         , Metadata      = metadataResult.Metadata!
                         , Parameters    = _parameterReader.Read(scriptPath).ToList()
                         , Status          = ToolLoadStatus.Valid
                   };
        }
        catch (Exception ex)
        {
            return new Models.ToolScript
                   {
                           ScriptPath = scriptPath
                         , Metadata = metadataResult.Metadata!
                         , Status = ToolLoadStatus.ParseError
                         , StatusMessage = ex.Message
                   };
        }
    }
}