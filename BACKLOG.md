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

**File:** `CognitivePlatform/Domains/Journal/JournalCommandParser.cs` — `ExtractIntValue`

**Observation:** `int.Parse(input.Trim())` throws `FormatException` if the value is
not a valid integer (e.g. `MoodScore: great`). Out-of-range integers (0, 6) are handled
gracefully by returning `null`, but malformed values crash the parser.

**Action:** Replace `int.Parse` with `int.TryParse`. Return `null` when parsing fails.
Add a test: `Parse_ReturnsNullMoodScore_WhenMoodScoreIsNonNumeric`.

---

### BACK-05 — Duplicate `Microsoft.AspNetCore.OpenApi` reference in `CognitivePlatform.Api.csproj`

**File:** `CognitivePlatform/CognitivePlatform.Api.csproj`

**Observation:** A `NU1504` warning fires on every build:
> Duplicate 'PackageReference' items found: Microsoft.AspNetCore.OpenApi 10.0.0

**Action:** Remove one of the two identical `<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.0" />` lines from the project file.

---

### BACK-06 — `JournalService.EditEntry`: `if (latest is null)` branch is dead code

**File:** `CognitivePlatform/Domains/Journal/JournalService.cs` — `EditEntry`

**Observation:** `GetLatestRevision` either returns a `JournalRevision` or throws
`InvalidOperationException`. It never returns `null`. The `if (latest is null) throw`
guard on the line after the call is therefore unreachable.

**Action:** Remove the dead null-check to keep the code honest. The exception from
`GetLatestRevision` is the correct signal for "no revisions found".
