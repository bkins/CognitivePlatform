using CognitivePlatform.Api.Avails;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers;

[ApiController]
[Route("api/models")]
public class ModelsController : ControllerBase
{
    private readonly LlmModelCatalog _catalog;

    public ModelsController(LlmModelCatalog catalog)
    {
        _catalog = catalog;
    }

    [HttpGet]
    public IActionResult Get() => Ok(_catalog.AvailableModels);
}
