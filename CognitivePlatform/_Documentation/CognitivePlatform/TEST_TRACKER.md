# Manual Test Tracker — Cognitive Platform

_Targeted manual tests for bug fixes and regressions. Run these against a live API instance (local or dev)._

Last updated: 2026-04-09

---

## How to use this file

Each test has a **Send** (the input to type in the LAA chat), an **Expected** result, and a **Pass / Fail** column.
Update the Status column after each run. Keep failed tests here until re-verified after a fix.

---

## BUG-07 — Groq 400 error: `JsonContent.Create` argument order

**Fixed:** 2026-04-08

| #     | Description                                  | Send                                                                              | Expected                                                            | Status |
| ----- | -------------------------------------------- | --------------------------------------------------------------------------------- | ------------------------------------------------------------------- | ------ |
| T07-1 | Groq responds successfully to a plain prompt | Any natural-language message (non-action) e.g. `"What is the capital of France?"` | A chat reply arrives without a 400 error in the logs                | ✅      |
| T07-2 | Groq fast-path action still works            | `"show my tasks"`                                                                 | Task list returned; no HTTP 400 in server logs                      | ✅      |
| T07-3 | Model probe endpoint passes                  | Start the API — observe startup logs                                              | `GroqLlmClient` probe succeeds (no 400/422 error in startup output) | ✅      |

---

## BUG-04 — Colon-prefix task block: `dueDateText` silently dropped

**Fixed:** 2026-04-08 — requires rebuild + ENH-05 for relative date values

| #     | Description                                | Send                                                            | Expected                                                                    | Status                                                                                                                                    |
| ----- | ------------------------------------------ | --------------------------------------------------------------- | --------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------- |
| T04-1 | Single-line task (no due date) still works | `"task: Buy milk"`                                              | Task created with description "Buy milk"; no due date                       | ✅                                                                                                                                         |
| T04-2 | Multiline block with DueDate               | `"task: Finish report`<br>`DueDate: Friday"` (two lines)        | Task created; due date set (requires rebuild + ENH-05 for day-name parsing) | ❌ Blocked: binary was pre-fix; after rebuild `DateTimeOffset.TryParse("Friday")` still fails — ENH-05 needed                              |
| T04-3 | Multiline block with multiple fields       | `"task: Call dentist`<br>`DueDate: tomorrow`<br>`tags: health"` | Task created with correct description, due date, and tags                   | ⚠️ LLM path correctly extracted `dueDateText=tomorrow`; tags ✅. Due date not stored — `TryParseDate("tomorrow")` fails. Blocked by ENH-05 |
| T04-4 | DueDate key is case-insensitive            | `"task: Review PR`<br>`duedate: Monday"`                        | Task created; due date applied (lowercase alias works)                      | ❌ Blocked: binary was pre-fix; after rebuild `DateTimeOffset.TryParse("Monday")` still fails — ENH-05 needed                              |
| T04-5 | No DueDate key — no regression             | `"task: Clean desk`<br>`details: top priority"`                 | Task created; due date is null; details applied                             | ✅ Task created as "Clean desk" with details wired correctly; garbled console output was telemetry interleaving, not a real failure        |

> **Action needed:** Rebuild and re-run T04-2 / T04-4 to confirm the remap fix. True pass requires ENH-05 (natural language date parsing).

---

## BUG-02 — Unhelpful fallback: "I'm not sure what to do next"

**Fixed:** 2026-04-08 — three distinct messages for three failure paths

Three distinct failure paths, each now has a tailored message.

### Path A — No action recognized

> Manual tests retired 2026-04-08: the LLM always routes unrecognised input through `ChitChat`, so this path is unreachable via normal prompts.
> Replaced with unit tests in `ConversationOrchestratorTests.cs` (CP.Workbench) that inject malformed interpreter responses directly.

| # | Description | Test method | Expected | Status |
|---|---|---|---|---|
| T02-A1 | Empty `ActionName` → correct no-action message | `ConverseAsync_WhenInterpreterReturnsEmptyActionName_ReturnsNoActionMessage` | Debug block starts with `"## No action recognized."` | ✅ 2026-04-09 |
| T02-A2 | Interpreter `Exception` failure → correct error message | `ConverseAsync_WhenInterpreterThrows_ReturnsSomethingWentWrongMessage` | Debug block starts with `"## Something went wrong while processing your request."` | ✅ 2026-04-09 |
| T02-A3 | Old fallback message never returned | `ConverseAsync_WhenInterpreterReturnsEmptyActionName_MessageNeverContainsOldFallback` | Response does NOT contain `"I'm not sure what to do next"` | ✅ 2026-04-09 |

### Path B — Missing required parameters, action does not allow clarification

> **Note:** This test must be run in **Release** mode (or with `_isDebug = false`) to see the production message. In Debug mode `_isDebug = true` and the verbose debug block is shown instead.

| #      | Description                                     | Send                                     | Expected (Release)                                                                                                   | Expected (Debug)                                                                                            | Status                                                                                                      |
| ------ | ----------------------------------------------- | ---------------------------------------- | -------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| T02-B1 | BatchAddTasks with empty body (no descriptions) | `"add tasks:"` (colon but nothing after) | `"I understood what you want to do, but I'm missing some required details. Could you rephrase with more specifics?"` | Verbose debug block starting with `"## Missing required parameters — action does not allow clarification."` | ✅ 2026-04-09 — Release build returns production message correctly |

### Path C — LLM interpreter throws an exception

| # | Description | Send | Expected | Status |
|---|---|---|---|---|
| T02-C1 | Trigger with LLM offline (stop Ollama/Groq) | Any non-fast-path message | `"Something went wrong while processing your request. Please try again."` | ☐ |
| T02-C2 | Old message never appears | Any exception path | Response does NOT contain `"I'm not sure what to do next"` | ☐ |

---

## Regression — General fast-path sanity

These confirm nothing was broken by the above fixes.

| #    | Description               | Send                          | Expected               | Status                                                                                       |
| ---- | ------------------------- | ----------------------------- | ---------------------- | -------------------------------------------------------------------------------------------- |
| TR-1 | Journal colon prefix      | `"journal: Felt great today"` | Journal entry created  | ✅                                                                                            |
| TR-2 | Slash command task add    | `"/task add Buy coffee"`      | Task created           | ✅                                                                                            |
| TR-3 | Natural language task add | `"add task call the bank"`    | Task created           | ✅                                                                                            |
| TR-4 | List tasks                | `"show my tasks"`             | Task list rendered     | ✅                                                                                            |
| TR-5 | Complete a task           | `"complete task 1"`           | Task 1 marked complete | ❌ API completed successfully (UTC timestamp noted); LAA app crashed silently after response — logged as BUG-08. Needs stack trace to diagnose. |
| TR-6 | Capabilities query        | `"what can you do"`           | Action list returned   | ✅                                                                                            |

---

## Legend

| Symbol | Meaning |
|---|---|
| ☐ | Not yet tested |
| ✅ | Pass |
| ❌ | Fail |
| ⚠️ | Pass with notes |
