namespace CognitivePlatform.Admin.Services.Testing;

public sealed record TestProcessResult(int? ExitCode, bool TimedOut = false);
