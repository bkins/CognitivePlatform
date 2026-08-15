using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace CognitivePlatform.Api.COCE;

public sealed class IncrementalObjectBuilder : IIncrementalObjectBuilder
{
    private readonly ConcurrentDictionary<string, IncrementalConstructionSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly IObjectConstructionEngine                                     _constructionEngine;
    private readonly IEnumerable<IObjectValidator>                                 _validators;

    public IncrementalObjectBuilder( IObjectConstructionEngine    constructionEngine
                                   , IEnumerable<IObjectValidator> validators )
    {
        _constructionEngine = constructionEngine ?? throw new ArgumentNullException(nameof(constructionEngine));
        _validators         = validators ?? Enumerable.Empty<IObjectValidator>();
    }

    public IncrementalConstructionSession GetOrCreateSession(string sessionId, Type targetType)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        return _sessions.GetOrAdd(
            sessionId
          , id => new IncrementalConstructionSession
                  {
                      SessionId      = id
                    , TargetType     = targetType
                    , CurrentJson    = "{}"
                    , CreatedAtUtc   = DateTimeOffset.UtcNow
                    , LastUpdatedUtc = DateTimeOffset.UtcNow
                  });
    }

    public bool ApplyIncrementalUpdate( string                     sessionId
                                      , string                     partialJson
                                      , out object?                constructedObject
                                      , out ObjectValidationResult validation )
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            constructedObject = null;
            validation        = ObjectValidationResult.Failure(new[] { $"No active construction session found for ID '{sessionId}'." });
            return false;
        }

        var mergedJson = MergeJson(session.CurrentJson, partialJson);
        session.CurrentJson    = mergedJson;
        session.LastUpdatedUtc = DateTimeOffset.UtcNow;

        try
        {
            constructedObject = _constructionEngine.Construct(mergedJson, session.TargetType);
        }
        catch (Exception ex)
        {
            constructedObject      = null;
            validation             = ObjectValidationResult.Failure(new[] { $"Failed to construct object from merged JSON: {ex.Message}" });
            session.LastValidation = validation;
            return false;
        }

        if (constructedObject is null)
        {
            validation             = ObjectValidationResult.Failure(new[] { "Constructed object was null." });
            session.LastValidation = validation;
            return false;
        }

        var validator = _validators.FirstOrDefault(v => v.CanValidate(session.TargetType));
        validation = validator?.Validate(constructedObject) ?? ObjectValidationResult.Success();
        session.LastValidation = validation;

        return validation.IsValid;
    }

    public bool TryGetCompletedObject(string sessionId, out object? completedObject)
    {
        completedObject = null;

        if (string.IsNullOrWhiteSpace(sessionId) || !_sessions.TryGetValue(sessionId, out var session))
            return false;

        if (session.LastValidation is not { IsValid: true })
            return false;

        try
        {
            completedObject = _constructionEngine.Construct(session.CurrentJson, session.TargetType);
            return completedObject is not null;
        }
        catch
        {
            return false;
        }
    }

    public void DiscardSession(string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId))
            _sessions.TryRemove(sessionId, out _);
    }

    private static string MergeJson(string originalJson, string incomingJson)
    {
        if (string.IsNullOrWhiteSpace(originalJson) || originalJson.Trim() == "{}")
            return incomingJson;

        if (string.IsNullOrWhiteSpace(incomingJson) || incomingJson.Trim() == "{}")
            return originalJson;

        JsonNode? originalNode;
        JsonNode? incomingNode;

        try
        {
            originalNode = JsonNode.Parse(originalJson);
            incomingNode = JsonNode.Parse(incomingJson);
        }
        catch
        {
            return incomingJson;
        }

        if (originalNode is JsonObject originalObj && incomingNode is JsonObject incomingObj)
        {
            MergeJsonObjects(originalObj, incomingObj);
            return originalObj.ToJsonString();
        }

        return incomingJson;
    }

    private static void MergeJsonObjects(JsonObject target, JsonObject source)
    {
        foreach (var property in source.ToList())
        {
            var key   = property.Key;
            var value = property.Value;

            if (value is null)
            {
                target[key] = null;
                continue;
            }

            if (target.TryGetPropertyValue(key, out var existingValue) && existingValue is JsonObject existingObj && value is JsonObject incomingChildObj)
            {
                MergeJsonObjects(existingObj, incomingChildObj);
            }
            else if (existingValue is JsonArray existingArr && value is JsonArray incomingArr)
            {
                foreach (var item in incomingArr.ToList())
                {
                    if (item is not null)
                    {
                        // Detach item from incoming array before appending to target
                        incomingArr.Remove(item);
                        existingArr.Add(item);
                    }
                }
            }
            else
            {
                // Detach from source before setting on target
                source.Remove(key);
                target[key] = value;
            }
        }
    }
}
