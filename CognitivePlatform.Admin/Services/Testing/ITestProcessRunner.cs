using System.Diagnostics;

namespace CognitivePlatform.Admin.Services.Testing;

public interface ITestProcessRunner
{
    Task<TestProcessResult> RunAsync(ProcessStartInfo startInfo, Action<string> output, TimeSpan timeout, CancellationToken cancellationToken);
}
