# Architecture — Cognitive Platform Universe

_Per-repo responsibilities, key structural decisions, and integration points._

Last updated: 2026-04

---

## Repository responsibilities

### `CognitivePlatform` (API)
**Owns:** All domain logic, LLM integration, persistence, and action execution.

No client-specific concerns live here. The API is environment-aware
(Development / QA / Production) and driven by `appsettings.{env}.json`.

Key namespaces:
- `Interpreter/` — FastPath resolver, LLM interpreter, mock interpreter
- `Orchestrator/` — Conversation orchestrator (the central coordinator)
- `Registry/` — Action registry (reflection-based discovery)
- `Execution/` — Execution engine (reflective invocation + DI resolution)
- `Domains/Tasks/` — Task domain: service, actions, repository, Eisenhower reasoner
- `Domains/Journal/` — Journal domain: service, command parser, revision repository
- `KnowledgeInbox/` — Cross-domain aggregation of tasks + journals
- `Data/` — `SqliteObjectStore`, idempotency store, per-environment SQLite DB files
- `Controllers/` — Thin HTTP layer; one controller per domain

### `LocalAIAssistant` (MAUI app)
**Owns:** All UI concerns — chat interface, journal/task views, knowledge inbox,
settings, logs page, app shell.

Communicates with the CP API exclusively via typed HTTP clients in `CognitivePlatform/CpClients/`.
Does not contain domain logic. Does not directly access the database.

Key areas:
- `CognitivePlatform/CpClients/` — one typed client per API domain
- `Knowledge/` — Inbox, journal views, task detail views (MVVM)
- `ViewModels/` — App-level ViewModels (chat, shell, logs, settings)
- `Views/Controls/` — `MarkdownView` (WebView-based), `TagRepeater`
- `Services/` — Logging (Serilog/JSONL), AI memory, environment/connectivity
- `PersonaAndContextEngine/` — **Parked legacy feature.** See `LAA-LEGACY.md`.

### `CP.Client.Core` (shared client library)
**Owns:** Client-side utilities shared between LAA and any future client.

Currently contains:
- `Common/ConnectivityToApi/` — `IConnectivityState`, `ConnectivityStatus` (used by LAA shell)
- `Intent/FastPathIntentDetector` — lightweight client-side FastPath pattern matching
- `Avails/` — Extensions shared across client projects
- `Web/Browser.cs` — Platform browser launch helper

**Charter question (open):** The boundary between what belongs in `CP.Client.Core`
vs directly in `LocalAIAssistant` is not yet formally defined. Current heuristic:
if a second client (web dashboard, CLI tool) would need it, it goes in Core.

### `CP.Shared.Primitives` (lowest-level shared code)
**Owns:** Extensions and utilities with zero dependencies beyond .NET base libraries.

- `Avails/Extensions/` — Bool, enum, string, list, task extensions
- `ConsoleSpinner` — used by SmokeTest

No domain models, no service interfaces, no DI concerns.

---

## LLM provider architecture

The API uses a factory pattern to select the active LLM provider at runtime:

```
LlmClientFactory
  → reads LlmClientSettings (from appsettings)
  → creates: OllamaLlmClient | GroqLlmClient
  → registered as ILlmClient (singleton)
```

Swapping providers = changing `LlmClient:Provider` in config. No code changes.

`IGroqUsageTracker` captures rate-limit headers from Groq responses and exposes
them via `GET /api/system/groq-usage`. The LAA shell header polls this and
displays a color-coded usage badge.

---

## Persistence architecture

All domain data lives in a per-environment SQLite file:

```
CognitivePlatform/Data/
  Development/platform.db
  QA/platform.db
  Prod/platform.db
```

`SqliteObjectStore` is the single persistence abstraction. It stores strongly typed
objects as JSON blobs with a (`type`, `id`) key. No schema migrations — new fields
are additive JSON changes.

**Soft delete invariant:** Every domain object has `IsDeleted` / `DeletedUtc`.
Hard deletes are never performed. This is enforced in services, not at the DB layer.

**Journal append-only invariant:** `JournalEntry` is immutable after creation.
All mutations create a new `JournalRevision`. `LatestRevision` is always the
authoritative content. This invariant must never be violated.

---

## Environment and connectivity model (LAA)

LAA uses a startup handshake to verify the CP API is reachable and the environment
version matches:

```
App launch
  → StartupHandshakeService
  → GET /api/system/environment
  → EnvironmentHandshakePolicy (version check, env match)
  → ConnectivityState updated
  → Shell header reflects connected / degraded / offline
```

`EnvironmentGuardHandler` is an `HttpMessageHandler` that blocks all API calls
when connectivity is in a failed state, preventing cascading errors.

---

## ReleaseConsole (DevOps tool)

A standalone .NET console application used as a lightweight CI/CD pipeline for
deploying the CP API to Dev, QA, and Prod environments. It is not part of the
domain architecture — it is a developer operations tool.

- Builds and publishes the CP API via `dotnet publish`
- Copies output to the target environment directory
- Supports `status` command to inspect current deployed versions

Source: `CP Universe / ReleaseConsole /` in Obsidian (not in this repo).
It will be formalized as development becomes more disciplined.

---

## Key decisions log

| Decision | Rationale |
|---|---|
| Schema-light object store over EF Core | Avoids migration friction during early development; domains evolve freely |
| Soft delete everywhere | Data trust — nothing irreversible at the storage layer |
| Journal append-only revisions | Edits are real, preserved, and reversible by construction |
| FastPath before LLM | Avoids unnecessary API calls for high-confidence patterns; latency + cost |
| LlmClientFactory singleton | Provider swap is a config change, not a code change |
| Separate repos for API / LAA / Core | Clean separation; LAA can be rebuilt without touching the API |
| `[NaturalLanguageAction]` attributes | Actions self-document; registry is auto-populated via reflection |
