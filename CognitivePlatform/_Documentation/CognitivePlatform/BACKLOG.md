# Backlog — Cognitive Platform

_Known bugs, UX issues, and planned enhancements. Ordered within each category by priority._

Last updated: 2026-04-19

---

## Bugs

| ID | Description | Area | Status |
|---|---|---|---|
| BUG-01 | UI bubble stuck at "Thinking ⏱" after FastPath execution | LAA / ChatViewModel | **Fixed 2026-04-10** — multi-core race: thinking frame `BeginInvokeOnMainThread` callback was posting unconditionally; late-queued frame overwrote the response. Added `if (!token.IsCancellationRequested)` guard inside the callback in `StartThinkingAsync`. |
| BUG-02 | Unhelpful fallback message: "I'm not sure what to do next" | CP API / Orchestrator | **Fixed 2026-04-08** — three distinct messages for Exception, MissingParams (no clarification), and no-action-recognized cases |
| BUG-03 | `/journal list` output renders as a single concatenated line | LAA / MarkdownView | **Fixed 2026-04-10** — single `\n` between entries was parsed by Markdig as a soft break (rendered as nothing); changed to `\n\n` (blank line) so each entry becomes its own Markdown paragraph block. |
| BUG-04 | Colon-prefix multiline param block: `dueDateText` not applied | CP API / FastPathResolver | **Fixed 2026-04-08** — remap `DueDate` → `dueDateText` after `ParseToDictionary` in task colon-prefix block |
| BUG-05 | Clear-vs-null ambiguity: cannot explicitly clear Tags or MoodScore via edit | CP API / JournalService | **Fixed 2026-04-10** — added `clearTags`, `clearMood`, `clearMoodScore` bool flags to `IJournalService.EditEntry` / `JournalService.EditEntry`. `null` still means "keep existing"; passing `clearX=true` explicitly sets the field to empty/null. Three new tests in `JournalServiceTests`. |
| BUG-06 | `Journal:` prefix persisted in entry text (not stripped at ingestion boundary) | CP API / JournalCommandParser | **Fixed 2026-04-07** — resolved in FastPath Grammar Refinement milestone |
| BUG-07 | Groq API 400 error: `JsonContent.Create(JsonOptions)` serializes options object instead of request body | CP API / GroqLlmClient | **Fixed 2026-04-08** — `JsonContent.Create(requestBody, options: JsonOptions)` |
| BUG-08 | LAA crashes silently after "complete task" response — no error message shown | LAA / NativeMarkdownView or ChatViewModel | **Fixed 2026-04-10** — WinUI cross-thread exception: `catch`/`finally` blocks in `ChatViewModel.SendAsync` and all `[ObservableProperty]` assignments in `UsageViewModel.ApplySnapshot` were running on a thread-pool thread after `ConfigureAwait(false)`. Wrapped all three sites in `MainThread.BeginInvokeOnMainThread`. |

---

## UX / UI issues

| ID | Description | Area | Status |
|---|---|---|---|
| UX-01 | Navigating away from app immediately submits active prompt | LAA | **Fixed 2026-04-10** — added `_isPageActive` flag set in `OnAppearing`/`OnDisappearing` in `MainPage.xaml.cs`. `OnEntryCompleted` returns early when the page is inactive, preventing keyboard-dismiss events fired during navigation from submitting the active prompt. |
| UX-02 | Chat does not scroll to bottom after new response | LAA / ChatView | **Fixed 2026-04-10** — `CollectionChanged` only fires on message add, not on in-place content change. Added `OnChatViewModelPropertyChanged` in `MainPage.xaml.cs` that watches `IsTyping`: when it goes `true → false` (turn complete), waits 100 ms for layout then calls `ScrollTo(lastMessage, End)`. |
| UX-03 | Assistant message bubbles grow but never shrink | LAA / MarkdownView | **Fixed 2026-04-10** — added `InvalidateMeasure()` at the end of `NativeMarkdownView.Render()`. MAUI's CollectionView caches item heights after first measurement; the explicit call propagates a re-measure up the visual tree so shorter responses shrink the bubble. |

---

## Enhancements

| ID | Description | Area | Status |
|---|---|---|---|
| ENH-01 | FastPath badge on assistant responses | LAA / ChatViewModel | **Done** — `⚡` label overlaid on message bubble with `IsVisible="{Binding WasFastPath}"`. `Message.WasFastPath` set from `response.WasFastPath` in `ChatViewModel.SendAsync`. |
| ENH-02 | Groq usage / rate-limit status indicator in shell header | LAA + CP API | **Done** |
| ENH-03 | Google Gemini API as fallback LLM provider | CP API / LlmClientFactory | **Done 2026-04-10** — `GeminiLlmClient` added using Google's OpenAI-compatible endpoint. `LlmProvider.Gemini` enum value, `GeminiSettings` config class, `"Gemini"` named HttpClient. Activate via `LlmClient:Provider = "Gemini"` and `LlmClient:Gemini:ApiKey = "..."` in appsettings / user-secrets. |
| ENH-04 | "Show my tasks" — clarify: active only vs all (exclude deleted) | CP API / TaskActions | **Fixed 2026-04-10** — `QueryTasks` called `_store.List<TaskItem>()` which returns all records including soft-deleted ones. Added `task.IsDeleted.Not()` filter as the first clause. |
| ENH-05 | Natural language due date parsing (relative dates: "tomorrow", "next Friday") | CP API / TaskActions | **Done 2026-04-10** — `TryParseDate` in `TaskActions` extended to handle: today/tomorrow/yesterday, named weekdays (mon–sun), "next \<weekday\>", "next week", "end of week / month", "in N days/weeks/months". Falls back to `DateTimeOffset.TryParse` for absolute formats. |
| ENH-06 | Add other free AI APIs as fallback LLM providers | CP API / LlmClientFactory | TODO -- Groq and Gemini are in place, but others to consider: GitHub, OpenRouter, Nvidia (NIM), Hugging Face, Cerebras Systems, Together AI, SiliconFlow, Fireworks AI, Mistral AI, DeepSeek AI, Cohere, others? |

---

## Daily Record Domain

| ID | Description | Area | Status |
|---|---|---|---|
| DR-01 | Phase D.1 — DailyRecord domain: open/checkpoint/close lifecycle, rollover | CP API / DailyRecord | **Done 2026-04-13** — Full domain implemented: `DailyRecord`, `DailyCheckpoint`, `DailyRecordService`, `DailyRecordCommandParser`, `DailyRecordActions`. FastPath routes `Plan:/Check:/EOD:` prefixes. Roll-forward tagging and `ClaimRolledOverTasks` action included. 53 new tests (28 parser, 25 service). |

---

## Larger Work Items / Epics / Milestones

| ID      | Description                                                  | Area              | Status / Notes                                               |
| ------- | ------------------------------------------------------------ | ----------------- | ------------------------------------------------------------ |
| EPIC-01 | **Rework LocalAiAssistant (LAA) UI**                         | LocalAiAssistant  | TODO --  Current UI is clunky and not well polished.  Need to plan, but a complete overhaul may be need. |
| EPIC-02 | **System Insights (Operational Intelligence)** → “How is the platform behaving?” | New UI (Web App?) | **Partially Done 2026-04-14** — `CognitivePlatform.Admin` (Blazor Server) delivers System Health, Registry Browser, Log Viewer, Data Management, Journal Admin, and Release Console. Remaining: structured telemetry persistence, deeper analytics, charting. |
| EPIC-03 | **User Insights (Behavioral / Cognitive Intelligence)** → “What does the data say about *the user’s life, patterns, and thinking*?” | New UI (Web App?) | TODO -- Plan details out.  After planning there may be overlap between EPIC-02, EPIC-03, and EPIC-04.  See conversation with ChatGPT: https://chatgpt.com/share/69dd359c-1f84-83e8-aa42-2b2ddd36e4f1 |
| EPIC-04 | **AI Influence and Autonomy**                                | CP API / New UI   | TODO -- Plan details out.  After planning there may be overlap between EPIC-02, EPIC-03, and EPIC-04.  See conversation with ChatGPT: https://chatgpt.com/share/69dd359c-1f84-83e8-aa42-2b2ddd36e4f1 |
| EPIC-05 | Reimplement LocalAiAssistant's Personalities and Short/Long term memory |                   | TODO -- Plan                                                 |

---

## CognitivePlatform.Admin — Technical Debt & Polish

Items discovered during the 2026-04-14 Admin UI build session.

| ID | Description | Area | Status |
|---|---|---|---|
| ADM-01 | `MudIconButton` uses `Title` attribute (MUD0002 analyzer warning) — should migrate to `Tooltip` | Admin / all pages | TODO — pre-existing pattern in SystemHealth.razor; affects RegistryBrowser and LogViewer too |
| ADM-02 | 36 `CS0618` warnings: API-local `BoolExtensions.Not()` is `[Obsolete]` — callers in `FastPathResolver`, `JournalActions`, `LlmInterpreter`, `ConversationOrchestrator` should use `CP.Shared.Primitives.Avails.Extensions` | CP API | TODO — not introduced in this session; pre-existing technical debt |
| ADM-03 | `KnowledgeItemDto.Tags` is typed as non-generic `IEnumerable` — should be `IEnumerable<string>` for type safety and consistent serialization | CP API / KnowledgeInbox | TODO |
| ADM-04 | `JournalRevisionRepository.GetRevisionsByEntryId` loads **all** `JournalRevision` records then filters in memory — O(n) full table scan; needs a `PartitionKey` or direct SQL lookup | CP API / Journal | TODO — fine at low scale, worth addressing before data grows |
| ADM-05 | Commented-out `/telemetry/logs` endpoint in `Program.cs` references `ConsoleTelemetrySink.InMemoryTelemetry` which never existed — remove the dead comment | CP API / Program.cs | TODO |
| ADM-06 | `appsettings.Development.json` in the Admin project is not gitignored at the project level — contains the admin secret; ensure secret is in user-secrets or that the file is excluded before the branch is shared | CognitivePlatform.Admin | TODO |
| ADM-07 | Release Console working directory must be manually set in `appsettings.Development.json` — consider auto-detecting solution root via `IHostEnvironment.ContentRootPath` traversal | Admin / ReleaseConsole | TODO |

---

## Porting / Integration follow-ups

| ID | Description | Area | Status |
|---|---|---|---|
| PORT-01 | Migrate `LocalAIAssistant.Services.EnvironmentGuardHandler` to use the canonical `CP.Client.Core.Web.EnvironmentGuardHandler` — update `MauiProgram.cs` DI registration and remove the LAA copy | LAA / MauiProgram.cs | TODO — LAA copy left in place for now to avoid MAUI DI complexity with typed descriptors |
| PORT-02 | Wire `TelemetryAggregatorService` to a real structured event store — currently returns empty list; requires adding `DurationMs` / `OperationName` to execution events and an `ITelemetryEventStore` accumulator | CP API / Telemetry | TODO — forward-compatible stub added; full wiring planned as part of EPIC-02 operational intelligence work |
| PORT-03 | Migrate LAA to use `CP.Client.Core.Web.ApiEnvironmentDescriptor` POCO for handler construction — keep MAUI `ObservableObject`-derived class for UI binding only | LAA | TODO — depends on PORT-01 |

---

## Backlog refinements

| ID | Description | Area | Status |
|---|---|---|---|
| BACK-01 | `TaskItem` has no `DeletedUtc` timestamp — only a boolean `IsDeleted` | CP API / TaskItem + TaskService | **Done 2026-04-10** — added `DateTimeOffset? DeletedUtc` to `TaskItem`; `TaskService.Delete` now sets both `IsDeleted = true` and `DeletedUtc = DateTimeOffset.UtcNow`. New test `Delete_SetsDeletedUtc_WhenTaskExists`. |
| BACK-02 | No dedicated `UpdateDueDate` method — callers do Get → mutate → Update | CP API / ITaskService + TaskService | **Done 2026-04-10** — added `TaskItem? UpdateDueDate(string id, DateTimeOffset? dueDate)` to `ITaskService` and `TaskService`. Pass `null` to clear. Four new tests in `TaskServiceTests`. |
| BACK-03 | `TaskKnowledgeSource.GetStatus` returned `Active` for completed+non-deleted tasks | CP API / TaskKnowledgeSource | **Done 2026-04-10** — fixed to return `KnowledgeStatus.Completed` for `CompletedAt != null && !IsDeleted`. Renamed and corrected test `GetKnowledgeItems_ReturnsCompletedStatus_ForCompletedNonDeletedTask`. |

---

## Notes

- BUG-05 and BUG-06 were documented in Obsidian `Bug Log - Tech Debt.md` but not
  previously in this file. Consolidated here as the single source of truth.
- BUG-07 root cause was identified in a prior session. Fix is a one-line change;
  deferred to avoid scope creep during ENH-02 implementation.
- ENH-02 is complete — retained here for historical tracking.
