using System;
using System.Collections.Generic;
using System.Linq;

namespace CognitivePlatform.Api.Integrations.CrossApp;

public sealed class ExternalAppConnectorRegistry
{
    private readonly IEnumerable<IExternalAppConnector> _connectors;

    public ExternalAppConnectorRegistry(IEnumerable<IExternalAppConnector> connectors)
    {
        _connectors = connectors;
    }

    public IExternalAppConnector? GetConnector(string appName)
    {
        return _connectors.FirstOrDefault(c => c.AppName.EqualsIgnoreCase(appName));
    }

    public IEnumerable<IExternalAppConnector> GetAllConnectors() => _connectors;
}
