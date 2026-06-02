using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Registry.Domains;

namespace CognitivePlatform.Api.Domains.BrainDump;

[Domain(typeof(BrainDumpDomain))]
public class BrainDumpActions
{
    private readonly IBrainDumpService _service;

    public BrainDumpActions(IBrainDumpService service)
    {
        _service = service;
    }

    // -----------------------------------------------------------------------
    // StartBrainDump
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Starts a new guided brain dump session. Prompts you through 7 therapeutic categories to unload mental weight: avoidance, fears, frustrations, discouragements, goals and barriers, hurt, and self-criticism."
      , Examples = new[]
                   {
                       "brain dump"
                     , "start a brain dump"
                     , "guided brain dump"
                     , "guided journal"
                     , "let's do a brain dump"
                     , "help me unload my thoughts"
                   }
      , Category = "braindump")]
    public string StartBrainDump()
    {
        var session = _service.StartSession();

        var sb = new StringBuilder();
        sb.AppendLine($"Brain dump session started. Session ID: {session.Id}");
        sb.AppendLine();
        sb.AppendLine("Let's unload some mental weight. Work through each category at your own pace — you can skip any section.");
        sb.AppendLine();
        sb.AppendLine("**1 of 7 — Things You're Putting Off (Avoidance)**");
        sb.AppendLine("What are you avoiding? What have you been meaning to do? What are you dreading?");

        return sb.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------------
    // GetLatestBrainDump
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Shows a summary of the most recent brain dump session, including which categories were filled and whether extraction has been run."
      , Examples = new[]
                   {
                       "show my last brain dump"
                     , "latest brain dump"
                     , "get my brain dump"
                     , "what was in my brain dump"
                   }
      , Category = "braindump")]
    public string GetLatestBrainDump()
    {
        var sessions = _service.ListSessions(count: 1);

        if (sessions.Count == 0)
            return "No brain dump sessions found. Say \"brain dump\" to start one.";

        return FormatSessionSummary(sessions[0]);
    }

    // -----------------------------------------------------------------------
    // ListBrainDumps
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(
        Description = "Lists your recent brain dump sessions with their dates and completion status."
      , Examples = new[]
                   {
                       "list brain dumps"
                     , "show my brain dumps"
                     , "brain dump history"
                     , "show recent brain dumps"
                   }
      , Category = "braindump")]
    public string ListBrainDumps()
    {
        var sessions = _service.ListSessions(count: 10);

        if (sessions.Count == 0)
            return "No brain dump sessions found. Say \"brain dump\" to start one.";

        var sb = new StringBuilder();
        sb.AppendLine($"Brain dumps ({sessions.Count} recent):");
        sb.AppendLine();

        foreach (var session in sessions)
        {
            var date      = session.CreatedAt.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
            var filled    = CountFilledCategories(session);
            var processed = session.Processed ? " [extracted]" : string.Empty;
            var idPreview = session.Id.Length > 8 ? session.Id[..8] + "…" : session.Id;
            sb.AppendLine($"• {date} — {filled}/7 categories{processed}  ({idPreview})");
        }

        return sb.ToString().TrimEnd();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string FormatSessionSummary(BrainDumpSession session)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Brain dump — {session.CreatedAt.ToLocalTime():MMM d, yyyy h:mm tt}");
        sb.AppendLine($"Session ID: {session.Id}");
        sb.AppendLine();

        AppendCategory(sb, "1. Things You're Putting Off", session.Avoidance);
        AppendCategory(sb, "2. Fears and Concerns",        session.Fears);
        AppendCategory(sb, "3. Anger / Frustrations",      session.Frustrations);
        AppendCategory(sb, "4. Discouragements",           session.Discouragements);
        AppendCategory(sb, "5. Goals and Barriers",        session.GoalsAndBarriers);
        AppendCategory(sb, "6. Hurt / Sorrow",             session.HurtAndSorrow);
        AppendCategory(sb, "7. Self-Criticism",            session.SelfCriticism);

        if (session.Processed && session.ExtractionSummary is not null)
        {
            sb.AppendLine();
            sb.AppendLine("**Extraction Summary**");
            sb.AppendLine(session.ExtractionSummary);
        }

        return sb.ToString().TrimEnd();
    }

    private static void AppendCategory(StringBuilder sb, string label, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        sb.AppendLine($"**{label}**");
        sb.AppendLine(text);
        sb.AppendLine();
    }

    private static int CountFilledCategories(BrainDumpSession session)
    {
        var count = 0;
        if (!string.IsNullOrWhiteSpace(session.Avoidance))        count++;
        if (!string.IsNullOrWhiteSpace(session.Fears))            count++;
        if (!string.IsNullOrWhiteSpace(session.Frustrations))     count++;
        if (!string.IsNullOrWhiteSpace(session.Discouragements))  count++;
        if (!string.IsNullOrWhiteSpace(session.GoalsAndBarriers)) count++;
        if (!string.IsNullOrWhiteSpace(session.HurtAndSorrow))    count++;
        if (!string.IsNullOrWhiteSpace(session.SelfCriticism))    count++;
        return count;
    }
}
