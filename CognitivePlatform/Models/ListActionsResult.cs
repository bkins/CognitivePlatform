using System.Collections.ObjectModel;

namespace CognitivePlatform.Api.Models;

/// <summary>
/// Uses IReadOnlyCollection<ActionMetadata> since IActionRegistry.Actions already exposes that.
/// This is for internal/meta use, logging, future dashboards, and downstream meta-actions.
/// </summary>
/// <param name="Metadata"></param>
/// <param name="Summary"></param>
public sealed record ListActionsResult
(
    IReadOnlyCollection<ActionMetadata> Metadata
  , string                              Summary
);