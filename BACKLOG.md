# BACKLOG

Items noted during the Phase 1 test catch-up session (2026-04-09). Each entry
describes an observed issue and the recommended action.

---

## Refactor Opportunities

### BACK-01 — `TaskService.Delete` does not set `DeletedUtc`

**Fixed (2026-04-13)**

`TaskItem.DeletedUtc` exists, `TaskService.Delete` assigns it, and
`TaskServiceTests` covers both `Delete_SetsIsDeletedTrue_WhenTaskExists` and
`Delete_SetsDeletedUtc_WhenTaskExists`.

---

### BACK-02 — `TaskService` has no `UpdateDueDate` method

**Fixed (2026-04-13)**

`TaskService.UpdateDueDate(string id, DateTimeOffset? dueDate)` is implemented and
covered by four tests in `TaskServiceTests`.

---

### BACK-03 — `TaskKnowledgeSource.GetStatus` returns `Active` for completed non-deleted tasks

**Fixed (2026-04-13)**

Decision: `CompletedAt != null && !IsDeleted` → `Completed`. Production code and
`TaskKnowledgeSourceTests.GetKnowledgeItems_ReturnsCompletedStatus_ForCompletedNonDeletedTask`
both reflect this. All 8 `TaskKnowledgeSourceTests` pass.

---

### BACK-04 — `JournalCommandParser.ExtractIntValue` throws on non-numeric `MoodScore`

**Fixed 2026-04-10**

`ExtractIntValue` already uses `int.TryParse`; malformed values return `null`.
Test `Parse_ReturnsNullMoodScore_WhenMoodScoreIsNonNumeric` in `JournalCommandParserTests` confirms the behaviour.

---

### BACK-05 — Duplicate `Microsoft.AspNetCore.OpenApi` reference in `CognitivePlatform.Api.csproj`

**Fixed (prior session)**

`CognitivePlatform.Api.csproj` contains only one `Microsoft.AspNetCore.OpenApi` reference;
the NU1504 duplicate warning no longer fires.

---

### BACK-07 — Pure display helpers in `UsageViewModel` / `TaskListParser` not directly testable

**Fixed 2026-04-10**

Created `LocalAIAssistant.Core` (`net9.0` class library):
- `LocalAIAssistant.Core.Parsing.TaskListParser` / `ParsedTask`
- `LocalAIAssistant.Core.Display.UsageDisplayFormatter` (`FormatHeaderSummary`, `GetColorCategory`)

MAUI project references `LocalAIAssistant.Core` (project ref). `LaaUnitTests` references it by
relative path (`../LocalAIAssistant/LocalAIAssistant.Core/`). Mirror classes removed from test
files; tests now exercise production code directly. 26/26 pass.

---

### BACK-06 — `JournalService.EditEntry`: `if (latest is null)` branch is dead code

**Fixed (prior session)**

The dead null-check after `GetLatestRevision` has been removed. The method throws
`InvalidOperationException` directly when no revisions exist — no null guard needed.
