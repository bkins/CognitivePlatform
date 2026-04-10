# Backlog — Cognitive Platform

_Known bugs, UX issues, and planned enhancements. Ordered within each category by priority._

Last updated: 2026-04

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
| UX-01 | Navigating away from app immediately submits active prompt | LAA | Open |
| UX-02 | Chat does not scroll to bottom after new response | LAA / ChatView | **Fixed 2026-04-10** — `CollectionChanged` only fires on message add, not on in-place content change. Added `OnChatViewModelPropertyChanged` in `MainPage.xaml.cs` that watches `IsTyping`: when it goes `true → false` (turn complete), waits 100 ms for layout then calls `ScrollTo(lastMessage, End)`. |
| UX-03 | Assistant message bubbles grow but never shrink | LAA / MarkdownView | Open |

---

## Enhancements

| ID | Description | Area | Status |
|---|---|---|---|
| ENH-01 | FastPath badge on assistant responses | LAA / ChatViewModel | Open |
| ENH-02 | Groq usage / rate-limit status indicator in shell header | LAA + CP API | **Done** |
| ENH-03 | Google Gemini API as fallback LLM provider | CP API / LlmClientFactory | Open |
| ENH-04 | "Show my tasks" — clarify: active only vs all (exclude deleted) | CP API / TaskActions | **Fixed 2026-04-10** — `QueryTasks` called `_store.List<TaskItem>()` which returns all records including soft-deleted ones. Added `task.IsDeleted.Not()` filter as the first clause. |
| ENH-05 | Natural language due date parsing (relative dates: "tomorrow", "next Friday") | CP API / FastPathResolver | Open |

---

## Notes

- BUG-05 and BUG-06 were documented in Obsidian `Bug Log - Tech Debt.md` but not
  previously in this file. Consolidated here as the single source of truth.
- BUG-07 root cause was identified in a prior session. Fix is a one-line change;
  deferred to avoid scope creep during ENH-02 implementation.
- ENH-02 is complete — retained here for historical tracking.
