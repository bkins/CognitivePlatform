# Phase 1 Test Catch-Up Session — Cognitive Platform

## Purpose

This document is a prompt / session plan for Claude Code to execute a structured
catch-up pass over the Cognitive Platform codebase and generate baseline unit tests
for all untested business logic. Read `CLAUDE.md` before beginning.

---

## Before You Start

1. Read `CLAUDE.md` in full — all coding style, naming, and test conventions apply here.
2. Run the existing tests to confirm green baseline:
   ```bash
   dotnet test src/CognitivePlatform.Tests/CognitivePlatform.Tests.csproj
   ```
3. Check whether Moq is already in the test `.csproj`. If not, add it:
   ```xml
   <PackageReference Include="Moq" Version="4.20.72" />
   ```
4. Do **not** install FluentAssertions or any other assertion library without asking.

---

## Catch-Up Pass Order

Work through the targets below in priority order. Complete and verify each one before
moving to the next. After each class, run `dotnet test` to confirm all tests pass.

---

### Priority 1 — Pure Logic (No Mocks Needed)

These classes have no external dependencies. Tests are fast to write and give the
highest confidence per line of effort.

#### `JournalCommandParser`
- File: `CognitivePlatform/Domains/Journal/JournalCommandParser.cs`
- Tests already exist in `JournalCommandParserTests.cs` — review for gaps only.
- Missing scenarios to consider:
  - `MoodScore:` directive parsing (valid range 1–5)
  - `MoodScore:` out of range (e.g. 0, 6) — should it be ignored or throw?
  - Mixed directives on a single line with text
  - Input with only directives and no body text
  - Whitespace-only input

#### `FastPathResolver`
- File: `CognitivePlatform/Interpreter/FastPath/FastPathResolver.cs`
- This is the highest-value target in the codebase — complex branching logic with
  many signal strings and parameter-building paths.
- Requires a mock `IActionRegistry`. Set it up once in the constructor and reuse.
- Test class: `FastPathResolverTests.cs`
- Cover each resolution mode:
  - Mode 0: capabilities query (`"what can you do"`, `"list actions"`)
  - Mode 1.1: colon prefix (`"journal: Had a good day"`, `"task: add Buy milk"`)
  - Mode 1.2: slash prefix (`"/journal add Had a good day"`, `"/task list"`, `"/task complete 1"`)
  - Mode 2 — task signals:
    - `TryResolveTaskAnalyze` — signal phrases
    - `TryResolveTaskList` — all signal phrases, filter parameter extraction
    - `TryResolveTaskComplete` — single task by ref
    - `TryResolveTaskCompleteBatch` — batch signal phrases
    - `TryResolveTaskDelete`
    - `TryResolveTaskUpdatePriority`
    - `TryResolveTaskUpdateDueDate`
  - Mode 3: generic fast path (attribute-driven)
  - Negative cases: unrecognised input should return `false`

---

### Priority 2 — Domain Services (Mocked `IObjectStore`)

These classes own business rules. Mock `IObjectStore` — do not touch SQLite.

#### `TaskService`
- File: `CognitivePlatform/Domains/Tasks/TaskService.cs`
- Test class: `TaskServiceTests.cs`
- Cover:
  - `Create` — assigns `Id` when empty, sets `CreatedAt` / `UpdatedAt`, assigns `SequenceNumber`
  - `Create` — preserves existing `Id` when already set
  - `CreateBatch` — creates all items, each gets unique `SequenceNumber`
  - `Get(Guid)` — throws `ArgumentException` when `Guid.Empty`
  - `Get(Guid)` — delegates to store correctly
  - `GetDeleted` — same guard as `Get`
  - `QueryTasks` — `includeCompleted` filter
  - `QueryTasks` — `onlyUrgent` filter
  - `QueryTasks` — `onlyImportant` filter
  - `QueryTasks` — `tag` filter (exact match, null tag = no filter)
  - `Complete` — sets `CompletedAt`, does not hard-delete
  - `Delete` — sets `IsDeleted` / `DeletedUtc`, does not hard-delete
  - `UpdateDueDate` — sets `DueDate` on existing task

#### `JournalService`
- File: `CognitivePlatform/Domains/Journal/JournalService.cs`
- Test class: `JournalServiceTests.cs`
- Four dependencies to mock: `IObjectStore`, `IJournalRevisionRepository`, `IJournalDraftRepository`, `ILogger<JournalService>`
- The `ILogger` mock can be `Mock<ILogger<JournalService>>()` — no setup needed, just pass `.Object`

**`AddEntryAsync`**
- Happy path — saves a `JournalEntry` and a `JournalRevision` to the store, and a `JournalDraft` via the draft repo
- Returns the `actualEntryId` from the store
- Logs a warning when the store returns a different Id than the one generated (simulate by having `_store.Save` return a different value)

**`EditEntry`**
- Happy path — creates a new `JournalRevision` that inherits fields from the latest when not supplied
- Partial edit — only supplied fields are overridden; null fields fall back to latest revision values
- Throws `KeyNotFoundException` when entry does not exist in store
- Throws `InvalidOperationException` when entry is soft-deleted (`DeletedUtc` is set)
- Throws `InvalidOperationException` when entry exists but has no revisions

**`GetEntry`**
- Throws `ArgumentException` when `id` is null or whitespace
- Returns `null` when store returns null (entry not found)
- Returns entry when found

**`GetById`**
- Throws `KeyNotFoundException` when entry not found
- Throws `InvalidOperationException` when entry has no revisions
- Returns correct `JournalEntryWithRevision` with `WasEdited = false` when one revision exists
- Returns correct `JournalEntryWithRevision` with `WasEdited = true` when more than one revision exists

**`ListEntries`**
- Returns entries ordered by `CreatedUtc` ascending
- Excludes entries where `DeletedUtc` is set (soft-deleted)
- Excludes entries that have no revisions (returns `null` from inner select, filtered out)
- Passes `fromUtc` / `toUtc` through to the store

**`DeleteEntry`**
- Returns `false` when entry not found
- Throws `InvalidOperationException` when already deleted
- Sets `DeletedUtc` and `DeletedReason` and saves — does NOT hard-delete

**`Exists`**
- Returns `true` when store returns an entry
- Returns `false` when store returns null

**`ListEntriesOnThisDay`**
- Returns only entries matching the given month and day
- Excludes soft-deleted entries
- Returns results ordered by `CreatedUtc` descending

**Static utility methods (no mocks needed — instantiate nothing)**
- `MapMoodLevel` — test all boundary values: ≤1 → VeryNegative, 2, 3, 4, ≥5 → VeryPositive
- `MapMoodEmoji` — test each `MoodLevel` value maps to the correct emoji, including the `_` fallback

---

### Priority 3 — Knowledge / Aggregation Layer

These have slightly more setup cost but cover important cross-domain logic.

#### `TaskKnowledgeSource`
- File: `CognitivePlatform/Domains/Tasks/TaskKnowledgeSource.cs`
- Mock `ITaskService` and `IObjectStore`
- Cover: `GetKnowledgeItems` — status mapping (Active vs Archived), tag mapping, filtering by `Id`

#### `KnowledgeService`
- File: `CognitivePlatform/KnowledgeInbox/KnowledgeService.cs`
- Mock `IKnowledgeSource` (one or two fakes)
- Cover: `GetKnowledge` — aggregates across sources, `Kind` filter, ordering by `LastModifiedAt`

---

### Priority 4 — Extensions and Utilities

Low risk but worth having to guard regressions.

#### `CP.Shared.Primitives` extension methods
- `StringExtensions` — `HasValue`, `HasNoValue`
- `BoolExtensions` — `.Not()`
- These are likely already tested implicitly — add explicit tests only if gaps exist.

---

## Session Rules

- **One class at a time.** Write all tests for a class, run `dotnet test`, confirm green, then move on.
- **Do not refactor source code** during this session unless a test reveals a genuine bug.
  If a refactor is needed, note it as a `BACKLOG.md` entry and keep moving.
- **Do not test MAUI ViewModels** — out of scope for this session.
- **Do not test `SqliteObjectStore`** — that is an integration concern.
- **Keep tests focused.** One behaviour per `[Fact]`. No omnibus tests that assert ten things.
- **Run `dotnet test` after every class.** Never accumulate failures across multiple files.
- After completing all priorities, run coverage and report which classes remain below 50%.

---

## Definition of Done for This Session

- [ ] `JournalCommandParserTests` — all gap scenarios covered
- [ ] `FastPathResolverTests` — all resolution modes covered, positive and negative
- [ ] `TaskServiceTests` — all public methods covered
- [ ] `JournalServiceTests` — all public methods covered
- [ ] `TaskKnowledgeSourceTests` — status mapping and filter covered
- [ ] `KnowledgeServiceTests` — aggregation and filter covered
- [ ] All tests pass (`dotnet test` exits 0)
- [ ] No new build warnings introduced
- [ ] `BACKLOG.md` updated with any refactor opportunities noted during testing
