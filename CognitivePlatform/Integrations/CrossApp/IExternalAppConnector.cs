using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CognitivePlatform.Api.Integrations.CrossApp;

public interface IExternalAppConnector
{
    string AppName { get; }
    bool IsConfigured { get; }
    Task<bool> PingAsync(CancellationToken ct = default);
    Task<object?> ExecuteActionAsync(string actionName, IDictionary<string, object> parameters, CancellationToken ct = default);
}
