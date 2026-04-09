# System — Cognitive Platform (CP)

_A conversational compiler that reliably transforms natural language into executable, validated, safe C# operations._

---

## What this system is

The Cognitive Platform is a server-side ASP.NET Core API that accepts natural language
input, interprets it into a structured action, and executes that action against a set of
well-defined domains. The system is designed to feel natural to use but behave
deterministically.

The central design principle:

> **LLM suggests. Engine decides.**

No LLM output reaches execution directly. Every intent passes through a registry lookup,
a safety gateway, and validated parameter resolution before anything executes.

---

## High-level flow

```
User Input
  → FastPathResolver       (zero-LLM path for known patterns)
  → LLM Interpreter        (intent extraction → structured JSON)
  → Clarification Loop     (missing / ambiguous parameters)
  → Safety Gateway         (validation, type coercion, permission checks)
  → Execution Engine       (reflective invocation via ActionRegistry)
  → Response Formatting    (human-friendly output)
```

---

## Repository map

| Repo | Purpose |
|---|---|
| `CognitivePlatform` | The API — all domain logic, LLM integration, persistence |
| `LocalAIAssistant` | MAUI client app — chat UI, journal/task views, knowledge inbox |
| `CP.Client.Core` | Shared client library — connectivity state, FastPath intent detection |
| `CP.Shared.Primitives` | Lowest-level shared extensions and models |


---

## Domains currently implemented

| Domain    | Actions / Services                                        |
|-----------|-----------------------------------------------------------|
| Tasks     | Add, list, complete, delete, update due date (Eisenhower) |
| Journals  | Add, list, edit (append-only revisions), search           |
| Knowledge | Cross-domain inbox aggregating tasks + journals           |
| System    | Health, version, environment info, LLM usage (Groq)       |

---

## Key architectural rules

- Actions are discovered via reflection using `[NaturalLanguageAction]` attributes.
- `FastPathResolver` handles unambiguous, high-confidence commands without an LLM call.
- Every domain operation flows through `ConversationOrchestrator`.
- Persistence uses a schema-light SQLite object store — strongly typed objects stored as
  JSON blobs, keyed by type + ID.
- **Soft delete is enforced everywhere.** Data is never permanently destroyed.
- Journal edits are **append-only** — each edit creates a new `JournalRevision`;
  no existing revision is ever modified.
- The LLM provider is runtime-swappable via config. Current active provider: **Groq**.
  Ollama is also supported. Gemini fallback is planned.

---

## What this system is not (yet)

- Not a general-purpose AI assistant — it executes defined, registered actions only.
- No calendar integration (planned Phase 5).
- No proactive suggestions (Insight Engine — spec complete, implementation deferred).
- No multi-user support or auth layer.
- No real-time push to clients.

---

## Related documents

| Document | Location |
|---|---|
| `ROADMAP.md` | This repo root — current phase status and what's next |
| `ARCHITECTURE.md` | This repo root — per-repo responsibilities, structural decisions |
| `BACKLOG.md` | This repo root — known bugs and deferred work |
| `STANDARDS.md` | This repo root — coding style and naming conventions |
| `LAA-LEGACY.md` | `LocalAIAssistant` repo root — parked LAA features and migration plan |
| Master Plan v2 | Obsidian: `CP Universe / _Core / Master Plan v2.md` |
| Insight Engine spec | Obsidian: `CP Universe / Milestones / Insight Engine Plan.md` |
