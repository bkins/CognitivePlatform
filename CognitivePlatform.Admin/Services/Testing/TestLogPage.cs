namespace CognitivePlatform.Admin.Services.Testing;

public sealed record TestLogPage(string[] Lines, int NextCursor, bool Truncated);
