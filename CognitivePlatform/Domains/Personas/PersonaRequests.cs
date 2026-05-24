using CognitivePlatform.Api.Domains.Personas.Models;

namespace CognitivePlatform.Api.Domains.Personas;

public record CreatePersonaRequest(string Name, string? ScenarioDescription);

public record AddMemoryRequest(string Content, MemoryType Type, bool UserAsserted);

public record UpdateMemoryStateRequest(MemoryState NewState);

public record CreateSnapshotRequest(string Name, string? Notes);

public record ContradictMemoryRequest(List<Guid> ContradictionMemoryIds);

public record CreateTimelineRequest(string Name, string Premise, DateTime BranchPointUtc);

public record AddTimelineEventRequest(string EventDate, string Description, float EmotionalWeight, bool IsCanon);
