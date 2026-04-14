using CognitivePlatform.Api.Registry;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace CognitivePlatform.Api.Controllers.Admin;

[Route("api/admin/registry")]
public sealed class AdminRegistryController : AdminControllerBase
{
    private readonly IActionRegistry _registry;

    public AdminRegistryController( IConfiguration  configuration
                                  , IActionRegistry  registry)
        : base(configuration)
    {
        _registry = registry;
    }

    /// <summary>
    /// Returns all registered actions as serialization-friendly DTOs,
    /// sorted by Category then Name. Reflection types (MethodInfo, Type)
    /// are mapped to their string equivalents.
    /// </summary>
    [HttpGet]
    public IActionResult GetRegistry()
    {
        if (IsAdminAuthorized().Not()) return Unauthorized401();

        var actions = _registry.Actions
                               .Select(action => new
                                       {
                                               action.Name
                                             , action.Description
                                             , action.Category
                                             , Examples            = action.Examples ?? []
                                             , action.IsFastPath
                                             , action.IsDestructive
                                             , action.AllowsClarification
                                             , Parameters          = action.Parameters
                                                                           .Select(parameter => new
                                                                                   {
                                                                                           parameter.Name
                                                                                         , TypeName    = parameter.ParameterType.Name
                                                                                         , parameter.Description
                                                                                         , parameter.IsOptional
                                                                                         , parameter.AllowEmpty
                                                                                         , DefaultValue = parameter.DefaultValue?.ToString()
                                                                                   })
                                       })
                               .OrderBy(action => action.Category)
                               .ThenBy(action => action.Name)
                               .ToList();

        return Ok(actions);
    }
}
