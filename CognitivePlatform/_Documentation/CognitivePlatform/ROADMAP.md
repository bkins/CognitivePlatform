# Roadmap — Cognitive Platform

_Current state of development. This document reflects reality, not aspiration._

Last updated: 2026-04

---

## Completed phases

### Phase 1 — Mechanical foundation ✅
Action registry, execution engine, reflective invocation, `[NaturalLanguageAction]`
attribute system, basic parameter handling.

### Phase 2 — LLM interpreter integration ✅
`LlmInterpreter` wired to `ConversationOrchestrator`. Ollama provider. Structured JSON
response parsing. Conversation context store.

### Phase 3 — Parameter semantics and validation ✅
Type coercion, boolean/enum/date parsing, optional + default parameters (Phase 3.9),
clarification loop for missing required params.

### Phase 3.x — FastPath resolver ✅
`FastPathResolver` handles unambiguous commands without an LLM call. Grammar-driven,
deterministic. Includes `UpdateTaskDueDate`, boolean toggles, and common task/journal
patterns.

### FastPath grammar refinement — journal metadata ✅
Block-only grammar parser in `JournalCommandParser`: extracts `Tags:`, `Mood:`,
`MoodScore:`, `Media:` from fast-path journal input. BUG-06 fixed (`Journal:` prefix
no longer persisted). Unquoted tags and mood now parse correctly. Completed 2026-04-07.
Known gap: `ExtractIntValue` uses `int.Parse` (fragile but benign) — logged in Scope Log.

### Phase 4.0 — Universal Object Store + Journals domain ✅
`SqliteObjectStore`, append-only `JournalRevision` model, Data Trust invariants enforced.
Edit/revert semantics locked. `JournalCommandParser` for structured journal input.

### Phase T.1 — Tasks domain ✅
`TaskItem`, `TaskService`, `TaskActions`, Eisenhower reasoner (`EisenhowerReasoner`).
Task priority matrix computed on read. Soft delete enforced.

### Groq integration + usage indicator (ENH-02) ✅
Runtime-swappable LLM provider via `LlmClientFactory`. Groq rate-limit headers captured
and exposed via `SystemController`. Color-coded usage badge in LAA shell header.

### UX Honesty ✅
Made the LAA UI accurately reflect journal state as stored. Tags, Mood, MoodScore
display when present. `IsEdited` badge shows correctly. Multi-line text renders without
collapsing. Known gap: journal detail API returns `state: 0` / no `isEdited` field —
`IsEdited` badge on detail page passes value via navigation parameter from inbox as a
workaround. Completed 2026-04-06.

---

## What's next

### Bug fixes — open items ⏳
See `BACKLOG.md` for the full list. Highest-confidence ready-to-fix items:
- **BUG-07** Groq API 400 error — root cause confirmed, one-line fix: `JsonContent.Create(requestBody, options: JsonOptions)`
- **BUG-04** Colon-prefix multiline `dueDateText` not applied in FastPathResolver
- **BUG-01** UI bubble stuck at "Thinking ⏱" after FastPath execution
- **BUG-05** Clear-vs-null ambiguity for Tags/MoodScore edits (design debt, deferred until patch semantics are scoped)

---

## Planned phases

### Phase D.1 — DailyRecord domain ⏳
Introduces a first-class `DailyRecord` aggregate that binds each day's journal
entries and tasks into a single coherent narrative spine. Implements the
three-phase temporal rhythm of a day: morning plan (`Plan:`), intraday
check-in (`Check:`), and evening close (`EOD:`). Computes and stores daily
metrics — completion rate, mood arc, planned vs. reactive task ratio — as
structured input for the Insight Engine.

**Dependencies:** Tasks domain (Phase T.1 ✅), Journal domain (Phase 4.0 ✅)
**Unlocks:** Insight Engine (Phase 5), food/wellness domains (future)
**Spec:** `CP.Workbench/Documentation/DailyRecord Domain Spec.md`

> **Master Plan note:** Add Phase D.1 to `CP Universe / _Core / Master Plan v2.md`
> between Phase T.1 and Phase 5. Spec is complete and ready to design against.

---

### Phase 5 — Assistant intelligence
- Insight Engine (proactive read-only reasoning layer) — **spec complete**, ready to build
  once UX Honesty is stable. See Obsidian: `CP Universe / Milestones / Insight Engine Plan.md`.
- Calendar awareness (Google Calendar + Outlook providers)
- `TaskReasoner` module (cross-domain prioritization)

### Phase 6 — Safety and permissions hardening
- Risk classification attributes on actions
- Mandatory confirmation gates for destructive operations
- Role-based permission checks
- Full audit trail

### Phase 7 — Extensibility and packaging
- Plugin model for third-party action domains
- NuGet packaging
- Developer templates and integration examples

---

## Parked / side quests

### Client-side offline intent queue
Deferred pending Phase 4+ persistence and sync design. Spec in Master Plan v2 Side Quests.

### LAA personality / LLM-switching engine
Parked in `LocalAIAssistant`. Planned migration to CP API in Phase 5+.
See `LocalAIAssistant/LAA-LEGACY.md` for details.

### ReleaseConsole (DevOps tool)
A .NET console app providing a lightweight CI/CD pipeline for deploying CP API to
Dev / QA / Prod environments. Active but used informally. Not part of the domain roadmap.
Docs in Obsidian: `CP Universe / ReleaseConsole /`.
