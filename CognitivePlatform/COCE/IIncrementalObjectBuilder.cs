using System;
using System.Collections.Generic;

namespace CognitivePlatform.Api.COCE;

public sealed record IncrementalConstructionSession
{
    public string                  SessionId      { get; init; } = string.Empty;
    public Type                    TargetType     { get; init; } = typeof(object);
    public string                  CurrentJson    { get; set; }  = "{}";
    public DateTimeOffset          CreatedAtUtc   { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset          LastUpdatedUtc { get; set; }  = DateTimeOffset.UtcNow;
    public ObjectValidationResult? LastValidation { get; set; }
}

public interface IIncrementalObjectBuilder
{
    IncrementalConstructionSession GetOrCreateSession(string sessionId, Type targetType);

    bool ApplyIncrementalUpdate(string sessionId
                              , string partialJson
                              , out object? constructedObject
                              , out ObjectValidationResult validation);

    bool TryGetCompletedObject(string sessionId, out object? completedObject);

    void DiscardSession(string sessionId);
}
