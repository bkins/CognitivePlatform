namespace CognitivePlatform.Admin.Services.Testing;

public sealed record TestProfile(string Id, string Title, string[] Roots, string? Filter = null);
