using CognitivePlatform.Admin.Services.ToolScript.Helpers;
using CognitivePlatform.Admin.Services.ToolScript.Interfaces;
using CP.Client.Core.Avails;
using Microsoft.Extensions.Options;

namespace CognitivePlatform.Admin.Services.ToolScript;

public sealed class ToolScriptService : IToolScriptService
{
    private readonly IToolScriptLoader _loader;
    private readonly string            _scriptsFolder;

    public ToolScriptService( IToolScriptLoader           loader
                            , IWebHostEnvironment         environment
                            , IOptions<ToolScriptOptions> options )
    {
        _loader  = loader;
        
        var baseRoot = options.Value.ScriptRoot ?? environment.ContentRootPath;

        _scriptsFolder = Path.Combine(baseRoot
                                    , options.Value.ScriptsDirectory);

        Console.WriteLine($"Script Path: {_scriptsFolder}");
    }

    public IReadOnlyList<Models.ToolScript> GetTools()
    {
        if (Directory.Exists(_scriptsFolder).Not()) return [];

        return Directory.EnumerateFiles(_scriptsFolder, "*.ps1")
                        .Select(_loader.Load)
                        .Where(script => script.Metadata.Hidden.Not())
                        .OrderBy(script => script.Metadata.Order)
                        .ThenBy(script => script.Metadata.Name)
                        .ToList();
    }
}