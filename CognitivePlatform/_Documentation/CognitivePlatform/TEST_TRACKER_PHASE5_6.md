# Manual Test Tracker — Phase 5 & Phase 6

_Run these against a live local API + LAA app.  
Start the API, open the LAA chat screen, and work through each section in order._

Last updated: 2026-04-11

---

## How to use this file

| Symbol | Meaning                       |
| ------ | ----------------------------- |
| ☐      | Not yet tested                |
| ✅      | Pass                          |
| ❌      | Fail — note what went wrong   |
| ⚠️     | Pass with caveats — note them |

For LAA tests, type the **Send** text exactly as written into the chat input and submit.  
For API tests, use the Scalar explorer at `http://localhost:5273/scalar` or a browser.

---

## ⚠️ Google Calendar — Not yet testable

You have stored credentials in user-secrets — that's the right first step.  
However, `GoogleCalendarProvider` has not been implemented yet (see `DEFERRED.md` item 1).  
There is nothing to test here until the provider code is written.  
**Skip all calendar tests for now.**

---

## Phase 6 — Safety & Governance

### P6-A — Destructive action confirmation gate (LLM path)

These tests go through the **LLM interpreter**, not the FastPath. Phrase the input naturally
(not as an exact command) so the LLM path is taken.

> **Setup:** Make sure you have at least one task and one journal entry before starting.

| #     | Description                                             | Send                                     | Expected                                                                          | Status |
| ----- | ------------------------------------------------------- | ---------------------------------------- | --------------------------------------------------------------------------------- | ------ |
| P6-A1 | Delete task via LLM path triggers confirmation          | `"I'd like to get rid of my first task"` | Response asks you to **confirm or cancel** — does NOT immediately delete          | ✅      |
| P6-A2 | Confirming a destructive action executes it             | After P6-A1, send: `"yes"`               | Task is deleted; confirmation response shown                                      | ✅      |
| P6-A3 | Cancelling a destructive action aborts it               | Repeat P6-A1, then send: `"no"`          | Response says **Cancelled** — task is NOT deleted (verify with `"show my tasks"`) | ✅      |
| P6-A4 | Unrecognised response re-prompts                        | Repeat P6-A1, then send: `"maybe"`       | Response asks you to **confirm or cancel** again (not a yes/no, so it loops)      | ✅      |
| P6-A5 | Delete journal entry via LLM path triggers confirmation | `"Remove my most recent journal entry"`  | Response asks you to confirm or cancel — does NOT immediately delete              | ❌      |
| P6-A6 | Confirming journal delete executes it                   | After P6-A5, send: `"confirm"`           | Entry is deleted                                                                  | ☐      |

---

### P6-B — FastPath delete behaviour (known bypass — documented)

The FastPath resolver runs before the confirmation gate.  
When you use an exact trigger phrase, the deletion executes immediately.  
This is expected behaviour for explicit commands.

| #     | Description                   | Send              | Expected                                                | Status |
| ----- | ----------------------------- | ----------------- | ------------------------------------------------------- | ------ |
| P6-B1 | FastPath delete bypasses gate | `"delete task 1"` | Task 1 deleted **immediately** — no confirmation prompt | ✅      |
| P6-B2 | FastPath delete with position | `"remove task 2"` | Task 2 deleted immediately                              | ✅      |

> **Note:** If you want confirmation even on FastPath deletes, see `DEFERRED.md` item 6 (Permission / Role-Based Action Gating) — adding a FastPath-aware confirmation step is the follow-on work.

---

### P6-C — Non-destructive actions are unaffected

Verify that ordinary actions still execute without any confirmation step.

| #     | Description                                             | Send                              | Expected                                            | Status |
| ----- | ------------------------------------------------------- | --------------------------------- | --------------------------------------------------- | ------ |
| P6-C1 | Creating a task does not prompt for confirmation        | `"add task pick up dry cleaning"` | Task created immediately — no confirm/cancel prompt | ✅      |
| P6-C2 | Listing tasks does not prompt for confirmation          | `"show my tasks"`                 | Task list returned immediately                      | ✅      |
| P6-C3 | Adding a journal entry does not prompt for confirmation | `"journal: Had a good morning"`   | Entry created immediately                           | ✅      |

---

### P6-D — Audit log (background verification)

The audit log is not yet exposed via a natural language action or HTTP endpoint.  
Verify it is working by inspecting the SQLite database directly.

| #     | Description                                  | How to verify                                                                                                                                                                                            | Expected                                                            | Status                                                                                                                                                                                                                             |
| ----- | -------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| P6-D1 | Successful action appends audit event        | Run any action (e.g. `"show my tasks"`), then open `platform.db` in a SQLite browser. Query: `SELECT * FROM objects WHERE type = 'AuditEvent' ORDER BY json_extract(data,'$.OccurredUtc') DESC LIMIT 5;` | Row exists with `ActionName = "ListTasks"`, `Outcome = "Success"`   | ⚠️ - I had to modify the SQL, but once that was done it passed.  Here is the modified SQL: `SELECT * FROM objects WHERE type = 'CognitivePlatform.Api.Audit.AuditEvent' ORDER BY json_extract(json,'$.OccurredUtc') DESC LIMIT 5;` |
| P6-D2 | Failed action appends audit event with error | Trigger a not-found error (e.g. `"delete task 999"`), then query as above                                                                                                                                | Row exists with `Outcome = "Failure"` and a non-null `ErrorMessage` | ❌ - See `More details on P6-D2:` below.                                                                                                                                                                                            |

> **Database location:** `CognitivePlatform\Data\Development\platform.db` (relative to the API project root).

More details on P6-D2:
* "Failure" was not found in the DB.
* Here are the console logs from the API:
```text
04/12/2026 02:57:43.74 AM [TELE] #002  Converse.Start | Session=9caf9147-71c0-486f-8486-1a68560fe069
        Input=delete task 999...
        Streaming: 🚫

04/12/2026 02:57:43.74 AM [TELE] #003  Orchestrator.Start | Session=9caf9147-71c0-486f-8486-1a68560fe069
        Model=qwen2.5:14b

04/12/2026 02:57:43.75 AM [TELE] #004  Orchestrator.Progress | Session=9caf9147-71c0-486f-8486-1a68560fe069
        FastPath.Resolved; Action=DeleteTask

04/12/2026 02:57:43.75 AM [TELE] #005  Execution.Start | Session=9caf9147-71c0-486f-8486-1a68560fe069
        ActionName=DeleteTask

04/12/2026 02:57:43.75 AM [TELE] #006  Execution.End | Session=9caf9147-71c0-486f-8486-1a68560fe069
        ActionName=DeleteTask
        Success=True
        Output=No active task found at position 999.

04/12/2026 02:57:43.76 AM [TELE] #007  Orchestrator.End | Session=9caf9147-71c0-486f-8486-1a68560fe069
        Response: Successfully executed FastPath-resolved action 'DeleteTask'
                  with parameters: taskReference: 999..; 
        
        Properties=DebugInfo=FastPath → Action=DeleteTask with Params=[taskReference: 999]

04/12/2026 02:57:43.76 AM [TELE] #008  Converse.Ended | Session=9caf9147-71c0-486f-8486-1a68560fe069
        Elapsed=0:00.0013

04/12/2026 02:57:43.87 AM [TELE] #  SystemController.Event | Session=
        Message: 'Usage endpoint was hit'
        Data:
                HasData: 'True'
                CapturedAt: '4/12/2026 2:09:01 AM +00:00'
                RequestsRemaining: '997'
                TokensRemaining: '5919'
                Resets: '4m19.2s (~7:13 PM) (Requests), 30.405s (Tokens)'
```

---

## Phase 5 — Assistant Intelligence

### P5-A — ReasonAboutTasks (LLM-assisted task Q&A)

> **Setup:** Have 3–5 tasks with a mix of priorities and due dates. Optionally add a few journal entries describing how you've been feeling.

| #     | Description                              | Send                                                                    | Expected                                                                                | Status |
| ----- | ---------------------------------------- | ----------------------------------------------------------------------- | --------------------------------------------------------------------------------------- | ------ |
| P5-A1 | Basic task reasoning question            | `"Which of my tasks should I focus on today?"`                          | LLM response references your actual tasks and gives a recommendation                    | ✅      |
| P5-A2 | Cross-domain reasoning (tasks + journal) | `"Given how I've been feeling lately, what tasks should I prioritise?"` | Response incorporates both task data and journal context                                | ✅      |
| P5-A3 | Workload analysis                        | `"Am I taking on too much right now?"`                                  | LLM provides an opinion based on the number and urgency of tasks                        | ✅      |
| P5-A4 | No tasks returns a graceful message      | Clear all tasks first, then send: `"What should I work on next?"`       | Response says something like **"You have no active tasks"** rather than calling the LLM | ✅      |

---

### P5-B — AnalyzePatterns (cross-domain insights)

> **Setup:** Have at least a week of journal entries and a mix of completed + active tasks.

| #     | Description                             | Send                                                                                                              | Expected                                                                      | Status |
| ----- | --------------------------------------- | ----------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- | ------ |
| P5-B1 | General pattern analysis                | `"What patterns do you see in my work habits?"`                                                                   | LLM response references tasks and/or journal entries and identifies a trend   | ✅      |
| P5-B2 | Focused analysis — mood                 | `"How does my mood relate to my productivity?"`                                                                   | Response specifically addresses mood (from journal) alongside task completion | ✅      |
| P5-B3 | Focused analysis — explicit focus label | `"Analyze my work-life balance"`                                                                                  | Response uses the phrase "work-life balance" as its focus                     | ✅      |
| P5-B4 | Week-in-review                          | `"Give me an overall picture of how my week went"`                                                                | LLM summarises the week using task + journal data                             | ✅      |
| P5-B5 | No data returns graceful message        | Clear all tasks and entries (or use a date range with no data), then send: `"Analyze my patterns from last year"` | Response says **"No tasks or journal entries found"** — LLM is not called     | ✅      |

---

### P5-C — Daily Brief (chat + HTTP endpoint)

The Daily Brief is now accessible both via the LAA chat and via the REST endpoint directly.

> **Setup:** Have at least one Important+Urgent task and one task with a due date of today or earlier.

**Chat triggers (FastPath — any of these phrases work):**

| #     | Description                     | Send                           | Expected                                                          | Status                                                                                                                                                                                                                                                                                                                                                                                                                                                                 |
| ----- | ------------------------------- | ------------------------------ | ----------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| P5-C1 | Chat trigger — direct phrase    | `"daily brief"`                | Brief returned immediately showing Do It Now + Due Today sections | ✅                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| P5-C2 | Chat trigger — natural phrase   | `"what's on my plate today?"`  | Brief returned                                                    | ⚠️ - Responds with "Added to queued: what's on my plate today?..."  The online indicator turned red, but the API was still active. No items were in queue.  I clicked on the indicator and then it turn green.  After further testing, all inputs are failing. Turns out that I had reached my Groq usage limit.  This needs to be more clear, and/or to automatically switch to Gemini, or Ollama. After waiting for Groq to reset, the Daily Brief report displayed. |
| P5-C3 | Chat trigger — morning briefing | `"morning briefing"`           | Brief returned                                                    | ✅                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |
| P5-C4 | Chat trigger — priorities       | `"show me today's priorities"` | Brief returned                                                    | ✅                                                                                                                                                                                                                                                                                                                                                                                                                                                                      |

**Content verification (via chat or `http://localhost:5273/api/tasks/brief`):**

| #      | Description                                   | How to test                                                                             | Expected                                                          | Status |
| ------ | --------------------------------------------- | --------------------------------------------------------------------------------------- | ----------------------------------------------------------------- | ------ |
| P5-C5  | Do It Now section shows Important+Urgent task | Ensure a task exists with IsImportant=true AND IsUrgent=true, then send `"daily brief"` | Brief lists that task under **Do It Now**                         | ✅      |
| P5-C6  | Do It Now shows (none) when no Q1 tasks       | No Important+Urgent tasks exist                                                         | **Do It Now** section shows **(none)**                            | ✅      |
| P5-C7  | Due Today section shows task due today        | Set a task's due date to today, then send `"daily brief"`                               | Task appears under **Due Today or Overdue** with no OVERDUE label | ✅      |
| P5-C8  | Overdue task shows OVERDUE label              | Set a task's due date to yesterday or earlier                                           | Task appears with **[OVERDUE]** label                             | ✅      |
| P5-C9  | Future task does not appear in Due Today      | Task due next week                                                                      | That task does NOT appear in the Due Today section                | ✅      |
| P5-C10 | Task in both sections                         | Task that is Important+Urgent AND due today                                             | Task appears in **both** Do It Now AND Due Today                  | ✅      |

---

### P5-D — Regression: Eisenhower AnalyzeTasks still works

Phase 5 added new reasoning actions. Make sure the existing Eisenhower action still works.

| #     | Description                                           | Send                    | Expected                                                                       | Status |
| ----- | ----------------------------------------------------- | ----------------------- | ------------------------------------------------------------------------------ | ------ |
| P5-D1 | Eisenhower matrix still renders                       | `"prioritize my tasks"` | Four-quadrant breakdown shown (Do It Now / Decide / Delegate / Delete)         | ✅      |
| P5-D2 | Eisenhower and ReasonAboutTasks give different output | Run P5-D1 then P5-A1    | P5-D1 = fixed-format matrix; P5-A1 = LLM prose — different styles, both useful | ☐      |

---

## General Regression

Run these after the above tests to confirm nothing was broken.

| # | Description | Send | Expected | Status |
|---|---|---|---|---|
| RG-1 | Create task | `"add task review the budget"` | Task created | ☐ |
| RG-2 | List tasks | `"show my tasks"` | Task list shown | ☐ |
| RG-3 | Complete task | `"complete task 1"` | Task 1 marked complete | ☐ |
| RG-4 | Add journal entry | `"journal: Felt focused this morning"` | Entry created | ☐ |
| RG-5 | Search journal | `"find journal entries about focus"` | Results shown (or "no entries found" if none match) | ☐ |
| RG-6 | Analyze journal | `"What have I been writing about lately?"` | LLM response summarising recent entries | ☐ |

---

## Known Gaps / Follow-on Items

| Item | Description | Reference |
|------|-------------|-----------|
| FastPath deletes bypass confirmation gate | "delete task 1" executes immediately | `DEFERRED.md` item 6 |
| Audit log not queryable via chat | No action or endpoint exposes audit events | Future: add `ListAuditEvents` action or admin endpoint |
| Google Calendar not implemented | Credentials stored; provider code not written | `DEFERRED.md` item 1 + `Google Calendar Setup.md` |
