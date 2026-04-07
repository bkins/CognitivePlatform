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
patterns. FastPath grammar refinement spec written (block-only grammar for journal
metadata) — implementation partially complete.

### Phase 4.0 — Universal Object Store + Journals domain ✅
`SqliteObjectStore`, append-only `JournalRevision` model, Data Trust invariants enforced.
Edit/revert semantics locked. `JournalCommandParser` for structured journal input.

### Phase T.1 — Tasks domain ✅
`TaskItem`, `TaskService`, `TaskActions`, Eisenhower reasoner (`EisenhowerReasoner`).
Task priority matrix computed on read. Soft delete enforced.

### Groq integration + usage indicator (ENH-02) ✅
Runtime-swappable LLM provider via `LlmClientFactory`. Groq rate-limit headers captured
and exposed via `SystemController`. Color-coded usage badge in LAA shell header.

---

## Current milestone: UX Honesty 🔄

**Goal:** Make the LAA UI accurately reflect journal state as stored today.

- Display Tags, Mood, MoodScore when present
- Show "Edited" badge when `IsEdited == true`
- Multi-line text renders without collapsing
- No editing UI yet — visibility only

**Status:** Spec complete (see Obsidian: `CP Universe / Milestones / UX Honesty.md`).
Implementation in progress in `LocalAIAssistant`.

---

## Up next

### FastPath grammar — journal metadata ⏳
Complete the block-only grammar parser in `JournalCommandParser`: extract `Tags:`,
`Mood:`, `MoodScore:`, `Media:` from fast-path journal input. Spec is locked.
See Obsidian: `CP Universe / Milestones / Fast-Path Grammar Refinement.md`.

### Bug fixes from Bug Log ⏳
Three known defects (see `BACKLOG.md`):
- Clear-vs-null ambiguity for tags and MoodScore in edits
- `Journal:` prefix persisted on initial entry text

---

## Planned phases

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
