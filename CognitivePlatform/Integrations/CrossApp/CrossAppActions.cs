using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Models;
using CognitivePlatform.Api.Execution;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CognitivePlatform.Api.Integrations.CrossApp;

public sealed class CrossAppActions
{
    private readonly ExternalAppConnectorRegistry _registry;

    public CrossAppActions(ExternalAppConnectorRegistry registry)
    {
        _registry = registry;
    }

    [NaturalLanguageAction(Description = "Adds an item to the WatchList application (e.g. movie, show).")]
    public async Task<ActionResult> AddWatchItem(string title, string? streamingService, string? category, string? type, CancellationToken ct)
    {
        var connector = _registry.GetConnector("WatchList");
        if (connector == null || !connector.IsConfigured)
        {
            return new ActionResult { Success = false, Message = "WatchList integration is not configured or enabled." };
        }

        var parameters = new Dictionary<string, object>
        {
            { "title", title },
            { "streamingService", streamingService ?? string.Empty },
            { "category", category ?? "Currently Watching" },
            { "type", type ?? "Movie" }
        };

        var success = await connector.ExecuteActionAsync("AddWatchItem", parameters, ct);
        if (success is bool ok && ok)
        {
            return new ActionResult { Success = true, Message = $"Successfully added '{title}' to your WatchList." };
        }

        return new ActionResult { Success = false, Message = $"Failed to add '{title}' to your WatchList." };
    }

    [NaturalLanguageAction(Description = "Lists items currently on your WatchList.")]
    public async Task<ActionResult> ListWatchItems(int? limit, CancellationToken ct)
    {
        var connector = _registry.GetConnector("WatchList");
        if (connector == null || !connector.IsConfigured)
        {
            return new ActionResult { Success = false, Message = "WatchList integration is not configured or enabled." };
        }

        var parameters = new Dictionary<string, object>
        {
            { "limit", limit ?? 10 }
        };

        var result = await connector.ExecuteActionAsync("ListWatchItems", parameters, ct);
        if (result is List<Dictionary<string, object>> items)
        {
            if (items.Count == 0)
            {
                return new ActionResult { Success = true, Message = "Your WatchList is currently empty." };
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Here are the items on your WatchList:");
            foreach (var item in items)
            {
                var type = item.TryGetValue("Type", out var t) ? t.ToString() : "Movie";
                var title = item.TryGetValue("Title", out var titleVal) ? titleVal.ToString() : "Unknown";
                var status = item.TryGetValue("IsWatched", out var iw) && iw is bool watched && watched ? "Watched" : "Unwatched";
                var ss = item.TryGetValue("StreamingService", out var ssVal) && ssVal != null ? ssVal.ToString() : string.Empty;
                var ssDisplay = string.IsNullOrEmpty(ss) ? string.Empty : $" on {ss}";
                
                sb.AppendLine($"- **{title}** ({type}) - {status}{ssDisplay}");
            }

            return new ActionResult { Success = true, Message = sb.ToString() };
        }

        return new ActionResult { Success = false, Message = "Failed to retrieve your WatchList." };
    }

    [NaturalLanguageAction(Description = "Marks a movie or show as completed/watched in the WatchList application.")]
    public async Task<ActionResult> CompleteWatchItem(string title, CancellationToken ct)
    {
        var connector = _registry.GetConnector("WatchList");
        if (connector == null || !connector.IsConfigured)
        {
            return new ActionResult { Success = false, Message = "WatchList integration is not configured or enabled." };
        }

        var parameters = new Dictionary<string, object>
        {
            { "title", title }
        };

        var success = await connector.ExecuteActionAsync("CompleteWatchItem", parameters, ct);
        if (success is bool ok && ok)
        {
            return new ActionResult { Success = true, Message = $"Successfully completed '{title}' in your WatchList." };
        }

        return new ActionResult { Success = false, Message = $"Could not find or complete '{title}' in your WatchList." };
    }
}
