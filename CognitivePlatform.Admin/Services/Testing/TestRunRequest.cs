using System.Text.Json.Serialization;

namespace CognitivePlatform.Admin.Services.Testing;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TestRunRequest(string Profile, Guid RequestId);
