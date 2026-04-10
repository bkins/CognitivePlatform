# BACKLOG

Items noted during the Phase 1 test catch-up session (2026-04-09). Each entry
describes an observed issue and the recommended action.

---

## Refactor Opportunities

### BACK-01 — `TaskService.Delete` does not set `DeletedUtc`

**File:** `CognitivePlatform/Domains/Tasks/TaskService.cs` — `Delete(Guid id)`

**Observation:** The soft-delete invariant documented in `CLAUDE.md` states every domain
object has `IsDeleted` / `DeletedUtc`. `TaskItem` exposes no `DeletedUtc` property, so
the service sets only `IsDeleted = true`. If a timestamp is needed for audit trails,
TTL policies, or the knowledge layer, a `DeletedUtc` field must be added to `TaskItem`
and assigned in `TaskService.Delete`.

**Action:** Add `DateTimeOffset? DeletedUtc` to `TaskItem`, assign it in `Delete`, and
add a test that asserts it is set to approximately `UtcNow`. Until then the delete test
in `TaskServiceTests` only asserts `IsDeleted = true`.

---

### BACK-02 — `TaskService` has no `UpdateDueDate` method

**File:** `CognitivePlatform/Domains/Tasks/TaskService.cs`

**Observation:** The session plan called for a test of `UpdateDueDate — sets DueDate on
existing task`, but no such method exists on `ITaskService` or `TaskService`. The
`FastPathResolver` routes `/task due` to `UpdateTaskDueDate` in the action registry, so
a handler presumably exists in `TaskActions.cs`, but it goes through `TaskService.Update`
(full object replace) rather than a dedicated patch method.

**Action:** Consider adding `TaskItem? UpdateDueDate(string id, DateTimeOffset? dueDate)`
to `ITaskService`/`TaskService` to make the intent explicit and improve testability.
Until then, callers must `Get` → mutate → `Update`.

---

### BACK-03 — `TaskKnowledgeSource.GetStatus` returns `Active` for completed non-deleted tasks

**File:** `CognitivePlatform/Domains/Tasks/TaskKnowledgeSource.cs` — `GetStatus`

**Observation:** The current logic is:
```
CompletedAt == null  → Active
CompletedAt != null AND IsDeleted → Deleted
CompletedAt != null AND !IsDeleted → Active   ← likely unintended
```
The comment in the code says "completed + archived (or soft-deleted)" should map to
`Archived`, but the actual code returns `Active` for a completed, non-deleted task. There
is also no `KnowledgeStatus.Archived` path reached. Tests document the current (possibly
buggy) behaviour; they should be updated once the intended mapping is clarified.

**Action:** Decide and document the correct mapping: should `CompletedAt != null &&
!IsDeleted` return `Completed`, `Archived`, or remain `Active`? Update the source and
the test together.

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
