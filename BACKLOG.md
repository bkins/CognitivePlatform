# Backlog — Cognitive Platform

_Known bugs, UX issues, and planned enhancements. Ordered within each category by priority._

Last updated: 2026-04

---

## Bugs

| ID | Description | Area | Status |
|---|---|---|---|
| BUG-01 | UI bubble stuck at "Thinking ⏱" after FastPath execution | LAA / ChatViewModel | Open |
| BUG-02 | Unhelpful fallback message: "I'm not sure what to do next" | CP API / Orchestrator | Open |
| BUG-03 | `/journal list` output renders as a single concatenated line | LAA / MarkdownView | Open |
| BUG-04 | Colon-prefix multiline param block: `dueDateText` not applied | CP API / FastPathResolver | Open |
| BUG-05 | Clear-vs-null ambiguity: cannot explicitly clear Tags or MoodScore via edit | CP API / JournalService | Open |
| BUG-06 | `Journal:` prefix persisted in entry text (not stripped at ingestion boundary) | CP API / JournalCommandParser | Open |
| BUG-07 | Groq API 400 error: `JsonContent.Create(JsonOptions)` serializes options object instead of request body | CP API / GroqLlmClient | Open — fix confirmed: `JsonContent.Create(requestBody, options: JsonOptions)` |

---

## UX / UI issues

| ID | Description | Area | Status |
|---|---|---|---|
| UX-01 | Navigating away from app immediately submits active prompt | LAA | Open |
| UX-02 | Chat does not scroll to bottom after new response | LAA / ChatView | Open |
| UX-03 | Assistant message bubbles grow but never shrink | LAA / MarkdownView | Open |

---

## Enhancements

| ID | Description | Area | Status |
|---|---|---|---|
| ENH-01 | FastPath badge on assistant responses | LAA / ChatViewModel | Open |
| ENH-02 | Groq usage / rate-limit status indicator in shell header | LAA + CP API | **Done** |
| ENH-03 | Google Gemini API as fallback LLM provider | CP API / LlmClientFactory | Open |
| ENH-04 | "Show my tasks" — clarify: active only vs all (exclude deleted) | CP API / TaskActions | Open |
| ENH-05 | Natural language due date parsing (relative dates: "tomorrow", "next Friday") | CP API / FastPathResolver | Open |

---

## Notes

- BUG-05 and BUG-06 were documented in Obsidian `Bug Log - Tech Debt.md` but not
  previously in this file. Consolidated here as the single source of truth.
- BUG-07 root cause was identified in a prior session. Fix is a one-line change;
  deferred to avoid scope creep during ENH-02 implementation.
- ENH-02 is complete — retained here for historical tracking.
