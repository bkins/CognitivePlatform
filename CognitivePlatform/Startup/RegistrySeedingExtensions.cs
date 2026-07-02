using CognitivePlatform.Api.Domains.Journal.Capabilities;
using CognitivePlatform.Api.Domains.System;
using CognitivePlatform.Api.Registry;
using CognitivePlatform.Api.Registry.Capabilities;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Startup;

/// <summary>
/// Seeds the programmatic Action and Capability registries once the app is built.
/// This is registry population, not DI registration, so it runs against the built
/// <see cref="WebApplication"/> rather than during <c>builder.Services</c> setup.
/// </summary>
public static class RegistrySeedingExtensions
{
    public static WebApplication SeedActionAndCapabilityRegistries(this WebApplication app)
    {
        // Programmatic actions registered via ActionDefinitionBuilder (ENH-22 Phase 2)
        var actionRegistry = app.Services.GetRequiredService<IActionRegistry>();
        actionRegistry.Register(new ActionDefinitionBuilder().Named("GetPlatformInfo")
                                                             .WithDescription("Returns current platform version and environment.")
                                                             .InDomain(new SystemDomain())
                                                             .WithExamples("What version is the platform?"
                                                                         , "What environment is this?")
                                                             .HandledBy(new PlatformInfoHandler())
                                                             .Build()
        );

        // Capability-registered actions (ENH-22 Phase 3)
        var capabilityRegistry = app.Services.GetRequiredService<ICapabilityRegistry>();
        capabilityRegistry.Register(new JournalSummaryCapability());

        // ENH-22 Phase 4: CRUD template pilot on Journal domain
        capabilityRegistry.Register(new JournalCrudCapability());

        return app;
    }
}
