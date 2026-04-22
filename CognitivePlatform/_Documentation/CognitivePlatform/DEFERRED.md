# DEFERRED.md — Items Intentionally Held Back

This file captures work that was consciously deferred during active development.
Each entry has enough detail to pick up and implement with minimal ramp-up discussion.

---

## 1. Google Calendar Integration

**Status:** Not started. Slot reserved in `DailyBriefService` and `TaskReasonerActions`.

**What was decided:**
- Google Calendar only for the first provider (Outlook/Exchange deferred further).
- Server-side OAuth dance (auth code flow): client opens a browser URL, user authorises,
  Google redirects to a callback endpoint on the API, which exchanges the code for tokens
  and stores them in the object store under a well-known key.
- `ICalendarProvider` interface with `GetEventsAsync(from, to)` — a single read method
  to start.
- Write actions (`AddCalendarEvent`, `RescheduleEvent`) added in a follow-on pass once
  read is stable.

**How to implement:**
1. Create a GCP project, enable the Google Calendar API, create OAuth 2.0 credentials
   (Web application type). Store `ClientId` / `ClientSecret` in `appsettings.json`
   (user secrets in dev). See `Google Calendar Setup.md` for step-by-step instructions.
2. Add `Google.Apis.Calendar.v3` NuGet package.
3. Create `CognitivePlatform/Integrations/Calendar/ICalendarProvider.cs` interface.
4. Create `GoogleCalendarProvider` implementation using the Google .NET client library.
   Store refresh tokens in `IObjectStore` (partition key: `calendar-tokens`).
5. Add `CalendarActions` class with `[NaturalLanguageAction]` methods:
   `GetTodayEvents()`, `GetEventsForDate(date)`, `AddCalendarEvent(title, date, time?)`.
6. Inject `ICalendarProvider` into `DailyBriefService` — add a third section
   "Today's Calendar" after the existing two.
7. Register `ICalendarProvider, GoogleCalendarProvider` in `Program.cs`.
8. Add OAuth callback endpoint: `GET /auth/google/callback?code=...`.

**Setup doc:** `Google Calendar Setup.md` (in this same directory).

---

## 2. ExecutionEngine Async Promotion

**Status:** ✅ **Done 2026-04-21.**
`IExecutionEngine.Execute` → `Task<string> ExecuteAsync(...)` with `CancellationToken`.
`UnwrapTaskResult` deleted; reflected `Task`/`Task<T>` results are now properly awaited.
`TakeTheFastPath` promoted to `async Task<ConverseResponse>`.
All 6 `_execution.Execute` call sites in `ConversationOrchestrator` updated.
Zero `GetAwaiter().GetResult()` calls remain in the CP API project.

---

## 3. IAuditLog Async Promotion

**Status:** ✅ **Done 2026-04-21** (bundled with item 2).
`IAuditLog.Append` removed; `AppendAsync(AuditEvent): Task` is the only write method.
`ObjectStoreAuditLog.AppendAsync` uses a proper `await _store.Save(...)`.
No sync callers existed; `Append` was deleted without a deprecation shim.

---

## 4. LogActivity — Explicit Habit / Activity Signals

**Status:** Not started. `AnalyzePatterns` infers patterns from tasks + journal text.
Explicit habit/activity logging would give sharper signals.

**What was decided:**
- Skip `LogActivity` for now; `AnalyzePatterns` MVP covers the core value.
- `LogActivity` adds explicit structured events: e.g. `LogActivity("run", duration: 30, unit: "minutes")`.
- These would be stored as `ActivityEvent` objects in `IObjectStore` and included as
  context in both `InsightsActions.AnalyzePatterns` and `TaskReasonerActions.ReasonAboutTasks`.

**How to implement:**
1. Create `ActivityEvent` domain object: `Id`, `OccurredUtc`, `ActivityType` (string),
   `Duration?`, `Unit?`, `Notes?`, `Tags`, `Meta`.
2. Create `IActivityLog` interface: `Log(ActivityEvent)`, `List(from?, to?)`.
3. Create `ObjectStoreActivityLog` backed by `IObjectStore`.
4. Create `ActivityActions` with:
   - `LogActivity(type, duration?, unit?, notes?, tags?)` — `[FastPath]`
   - `ListActivities(fromDate?, toDate?)` — `[FastPath]`
5. Inject `IActivityLog` into `InsightsActions` and append activity data to the context
   prompt as a third section after tasks and journal entries.
6. Register in `Program.cs`.

---

## 5. KnowledgeService.ListHeaders Optimisation

**Status:** `NotImplementedException` — both `JournalKnowledgeSource.ListHeaders` and
`TaskKnowledgeSource.ListHeaders` throw.

**Why deferred:** No consumer of `ListHeaders` exists yet; premature optimisation.

**How to implement when needed:**
- `JournalKnowledgeSource.ListHeaders`: call `_journal.ListEntries()`, project to
  `KnowledgeHeader` (id, title = first 80 chars of text, createdUtc, status).
- `TaskKnowledgeSource.ListHeaders`: call `_taskService.GetOrderedActiveTasks()`,
  project to `KnowledgeHeader` (id, title = ShortDescription, createdUtc, status).

---

## 6. Permission / Role-Based Action Gating

**Status:** `[DestructiveAction]` attribute and two-tier Safe/Destructive model implemented.
Role-based permissions not started.

**What was decided:**
- Start with two tiers: `Safe` (default) and `Destructive` (requires confirmation).
- Role-based control deferred until there is more than one user or a clear multi-user
  scenario.

**How to implement when needed:**
1. Add `[RequiresPermission("admin")]` attribute (or similar) to `NaturalLanguageActionAttribute`
   or as a separate attribute alongside `[DestructiveAction]`.
2. Add `ActionMetadata.RequiredPermission` (nullable string).
3. In `ConversationOrchestrator`, after action selection, check if the session context
   carries the required permission claim. Reject with a clear message if not.
4. Add a permission claim to `ConversationContext.Metadata` ("role", "permissions", etc.)
   — populated from auth middleware or a session-establishment step.

---

## 7. Calendar Data in DailyBriefService

**Status:** `DailyBriefService.GetBrief()` has two sections (Do It Now, Due Today).
A third "Today's Calendar" section is reserved.

**Depends on:** Item 1 (Google Calendar Integration).

**How to implement:**
1. Add optional `ICalendarProvider?` to `DailyBriefService` constructor (nullable so the
   brief still works when no calendar is configured).
2. After the Due Today section, append:
   ```
   --- Today's Calendar ---
   • 09:00  Sprint planning (1 hr)
   • 14:30  1-on-1 with Alex
   ```
3. If `_calendarProvider` is null or throws, omit the section gracefully (log a warning).

---

## 8. Calendar Read/Write Actions

**Status:** Not started. Depends on Item 1.

**Actions to add in `CalendarActions`:**
- `GetTodayEvents()` — `[FastPath]`
- `GetEventsForDate(date)` — `[FastPath]`
- `AddCalendarEvent(title, date, time?, durationMinutes?)` — `[FastPath]`
- `FindFreeTime(date, durationMinutes)` — LLM-assisted

---

## 9. Calendar Context in TaskReasonerActions

**Status:** `TaskReasonerActions.ReasonAboutTasks` uses tasks + journal only.
Calendar was explicitly excluded pending integration.

**How to implement when calendar exists:**
1. Inject `ICalendarProvider?` into `TaskReasonerActions`.
2. If provider is available, fetch today's events and append a third section
   "=== Today's Calendar ===" to the context prompt before the user's question.

---

_Last updated: 2026-04-11_
