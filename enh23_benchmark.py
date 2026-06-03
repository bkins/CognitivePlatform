"""ENH-23 Phase 1 — Ollama model benchmark for CP interpreter evaluation.

Three test tiers:
  1. LATENCY  — short prompt (~100 tokens), raw inference speed
  2. SCHEMA   — medium prompt (~900 tokens), 8 representative CP actions
  3. CONTEXT  — full CP system.txt prompt (~5 000 tokens), context limit probe
"""
import json
import time
import urllib.request
import urllib.error
import urllib.parse
import datetime
import sys
import io

OLLAMA_BASE = "http://localhost:11434"

# General-purpose instruction models only.
# Excluded: codellama/deepseek-coder/qwen2.5-coder (code-specific), nomic-embed (embedding only),
#           llama3:latest (superseded by llama3.1), deepseek-r1 (reasoning/CoT — generates 500+ tokens per response)
MODELS_TO_TEST = [
    "phi3:mini",
    "mistral:latest",
    "qwen2.5:7b",
    "llama3.1:8b",
    "qwen2.5:14b",
]

TODAY     = datetime.date.today()
YESTERDAY = TODAY - datetime.timedelta(days=1)
LAST_MON  = TODAY - datetime.timedelta(days=(TODAY.weekday() + 7))
LAST_SUN  = LAST_MON + datetime.timedelta(days=6)

# ─── Tier 1: Short latency prompt (no system prompt, just role + format) ─────

SHORT_SYSTEM = """You are a command router. Reply ONLY with valid JSON.
Format: {"actionName":"X","parameters":{},"reason":"","failureType":"None"}
Actions: AddTask(shortDescription), CompleteTask(taskReference), ChitChat(text), ListActions, AddJournalEntry(text)"""

# ─── Tier 2: Medium schema prompt (~900 tokens) ───────────────────────────────

ACTIONS_BLOCK = """Action: ChitChat
  Description: Respond to conversational input, greetings, or casual messages.
  Parameters:
    - text (string, required=true, allowEmpty=false): "The user's conversational message."
  Examples: hello / how are you

Action: AddTask
  Description: Add a single new task.
  Parameters:
    - shortDescription (string, required=true, allowEmpty=false): "Brief description of the task."
  Examples: add task buy milk / I need to buy groceries

Action: CompleteTask
  Description: Mark a single task as complete by its position number.
  Parameters:
    - taskReference (string, required=true, allowEmpty=false): "Task number as a string e.g. '2'."
  Examples: task 2 is done / complete task 1

Action: BatchAddTasks
  Description: Add multiple tasks at once. Use ONLY when user provides 2+ tasks in one message.
  Parameters:
    - descriptions (string, required=true, allowEmpty=false): "Task descriptions separated by '|'."
  Examples: add tasks: buy milk, call dentist / I need to do: review code, fix bug

Action: AddJournalEntry
  Description: Add a new journal entry.
  Parameters:
    - text (string, required=true, allowEmpty=false): "The journal entry text."
  Examples: journal: had a productive day / add journal entry about today

Action: ListJournalEntries
  Description: List journal entries, optionally filtered by date range.
  Parameters:
    - fromDate (string, required=false, allowEmpty=false): "Start date yyyy-MM-dd."
    - toDate (string, required=false, allowEmpty=false): "End date yyyy-MM-dd."
  Examples: show journal entries from last week / list my journal entries

Action: ListActions
  Description: Show all available actions and capabilities.
  Parameters: (none)
  Examples: what can you do? / list your commands

Action: DescribeAction
  Description: Explain what a specific action does.
  Parameters:
    - actionName (string, required=true, allowEmpty=false): "The exact name of the action."
  Examples: describe AddTask / how does AddJournalEntry work"""

SESSION_STATE = f"""SESSION STATE: Today={TODAY} ({TODAY.strftime('%A')}), Yesterday={YESTERDAY}, Last week={LAST_MON} to {LAST_SUN}"""

MEDIUM_SYSTEM = f"""SYSTEM:
You are the Natural Language Command Interpreter for the CognitivePlatform.
Your job is to SELECT the correct action AND extract the correct parameters.
You NEVER execute actions — you only choose the action and its parameters.

Your response MUST ALWAYS be ONLY valid JSON. No commentary. No text before or after.

REQUIRED JSON FORMAT:
{{
  "actionName": "NameOfActionOrNone",
  "parameters": {{ "paramName": "value" }},
  "reason": "Short explanation.",
  "failureType": "None | NoMatchingAction | AmbiguousIntent | MissingParameters",
  "candidateActions": ["Optional list when ambiguous"],
  "missingParameters": ["Optional list of missing required params"]
}}

STRICT RULES:
- Output ONLY valid JSON — no preamble, no markdown fences, no commentary.
- If a required parameter is missing, set actionName to "none" and failureType to "MissingParameters".
- Boolean parameter values MUST be strings: "true" / "false" not true / false.
- Never invent parameter values not stated by the user.
- If multiple tasks are given in one message, use BatchAddTasks with descriptions joined by "|".
- Conversational/greeting input → ChitChat.
- "What can you do?" → ListActions.
- "Describe/explain <action>" → DescribeAction.

AVAILABLE ACTIONS:
{ACTIONS_BLOCK}

{SESSION_STATE}

FEW-SHOT EXAMPLES:
User: add task buy milk
Assistant: {{"actionName":"AddTask","parameters":{{"shortDescription":"Buy milk"}},"reason":"User wants to add a task.","failureType":"None"}}

User: add a task
Assistant: {{"actionName":"none","parameters":{{}},"reason":"Missing required parameter.","failureType":"MissingParameters","candidateActions":["AddTask"],"missingParameters":["shortDescription"]}}

User: task 2 is done
Assistant: {{"actionName":"CompleteTask","parameters":{{"taskReference":"2"}},"reason":"User marked task 2 complete.","failureType":"None"}}

USER:
{{USER_INPUT}}

SYSTEM: Return ONLY the JSON. Nothing else."""

# ─── Tier 3: Full CP system prompt (the actual file) ─────────────────────────

def load_full_system_prompt(user_input: str) -> str:
    base = open(r"CognitivePlatform\Prompts\system.txt", encoding="utf-8").read()
    base = base.replace("{{ACTIONS}}", ACTIONS_BLOCK)
    base = base.replace("{{SESSION_STATE}}", SESSION_STATE)
    base = base.replace("{{USER_INPUT}}", user_input)
    return base


# ─── Ollama HTTP helpers ──────────────────────────────────────────────────────

def _post_streaming(model: str, prompt: str, timeout: int, num_ctx: int = 8192):
    """
    POST to Ollama /api/generate (streaming).
    Returns (ttft_ms, total_ms, tps, full_text, eval_count, prompt_tokens, error_msg).
    On hard error: all timing fields are None, error_msg is set.
    """
    payload = json.dumps({
        "model":  model,
        "prompt": prompt,
        "stream": True,
        "options": {
            "temperature": 0.0,
            "seed":        42,
            "num_predict": 512,
            "num_ctx":     num_ctx,
        },
    }).encode("utf-8")

    req = urllib.request.Request(
        f"{OLLAMA_BASE}/api/generate",
        data    = payload,
        headers = {"Content-Type": "application/json"},
        method  = "POST",
    )

    t_start = time.perf_counter()
    ttft_ms = None
    full_response = ""
    final_stats   = {}

    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            for raw_line in resp:
                line = raw_line.decode("utf-8").strip()
                if not line:
                    continue
                try:
                    chunk = json.loads(line)
                except json.JSONDecodeError:
                    continue
                token = chunk.get("response", "")
                if token and ttft_ms is None:
                    ttft_ms = (time.perf_counter() - t_start) * 1000
                full_response += token
                if chunk.get("done"):
                    final_stats = chunk
                    break

    except urllib.error.HTTPError as exc:
        body = ""
        try:
            body = exc.read(500).decode("utf-8", errors="replace")
        except Exception:
            pass
        return None, None, None, None, 0, 0, f"HTTP {exc.code}: {exc.reason} — {body[:200]}"

    except TimeoutError:
        return None, None, None, None, 0, 0, "TIMEOUT"

    except Exception as exc:
        return None, None, None, None, 0, 0, f"{type(exc).__name__}: {exc}"

    total_ms     = (time.perf_counter() - t_start) * 1000
    eval_count   = final_stats.get("eval_count", 0)
    prompt_count = final_stats.get("prompt_eval_count", 0)
    eval_dur_ns  = final_stats.get("eval_duration", 1) or 1
    tps          = eval_count / (eval_dur_ns / 1e9)

    return ttft_ms, total_ms, tps, full_response.strip(), eval_count, prompt_count, None


# ─── Schema scoring ───────────────────────────────────────────────────────────

SCHEMA_TESTS = [
    {
        "id": 1, "utterance": "add task buy milk",
        "expect_action": "AddTask",
        "expect_params": {"shortDescription": "Buy milk"},
        "expect_failure": "None",
    },
    {
        "id": 2, "utterance": "task 2 is done",
        "expect_action": "CompleteTask",
        "expect_params": {"taskReference": "2"},
        "expect_failure": "None",
    },
    {
        "id": 3, "utterance": "add tasks: buy milk, call dentist, finish report",
        "expect_action": "BatchAddTasks",
        "expect_params_key": "descriptions",
        "expect_failure": "None",
    },
    {
        "id": 4, "utterance": "add a task",
        "expect_action": "none",
        "expect_failure": "MissingParameters",
        "expect_candidate": "AddTask",
        "expect_missing": "shortDescription",
    },
    {
        "id": 5, "utterance": "journal: had a productive day at work",
        "expect_action": "AddJournalEntry",
        "expect_params_key": "text",
        "expect_failure": "None",
    },
    {
        "id": 6, "utterance": "what can you do?",
        "expect_action": "ListActions",
        "expect_failure": "None",
    },
    {
        "id": 7, "utterance": "hello there",
        "expect_action": "ChitChat",
        "expect_failure": "None",
    },
    {
        "id": 8, "utterance": "describe the AddTask action",
        "expect_action": "DescribeAction",
        "expect_params": {"actionName": "AddTask"},
        "expect_failure": "None",
    },
]


def _extract_json(text: str) -> str:
    cleaned = text.strip()
    if cleaned.startswith("```"):
        lines   = cleaned.split("\n")
        cleaned = "\n".join(l for l in lines if not l.startswith("```"))
    f = cleaned.find("{")
    l = cleaned.rfind("}")
    return cleaned[f:l+1] if f >= 0 and l > f else cleaned


def score_test(test: dict, response_text: str) -> tuple[int, dict]:
    """Returns (score 0–10, details)."""
    details  = {"raw_excerpt": response_text[:300]}
    score    = 0

    try:
        parsed = json.loads(_extract_json(response_text))
        score += 2
        details["valid_json"] = True
    except Exception as exc:
        details["valid_json"]   = False
        details["parse_error"]  = str(exc)
        return 0, details

    action_name  = parsed.get("actionName", "")
    failure_type = parsed.get("failureType", "")
    params       = parsed.get("parameters", {})
    candidates   = parsed.get("candidateActions", [])
    missing      = parsed.get("missingParameters", [])

    details.update(actionName=action_name, failureType=failure_type, params=params)

    # Action name (3 pts)
    if action_name.lower() == test["expect_action"].lower():
        score += 3
        details["action_correct"] = True
    else:
        details["action_correct"]  = False
        details["action_expected"] = test["expect_action"]

    # failureType (2 pts)
    if failure_type.lower() == test["expect_failure"].lower():
        score += 2
        details["ft_correct"] = True
    else:
        details["ft_correct"]  = False
        details["ft_expected"] = test["expect_failure"]

    # Parameters (3 pts)
    p_score = 0
    if "expect_params" in test:
        hits  = sum(1 for k, v in test["expect_params"].items() if params.get(k, "").lower() == v.lower())
        total = len(test["expect_params"])
        p_score = round((hits / total) * 3) if total else 3
    elif "expect_params_key" in test:
        p_score = 3 if (test["expect_params_key"] in params and params[test["expect_params_key"]]) else 0
    elif "expect_candidate" in test:
        p_score  = (2 if test["expect_candidate"] in candidates else 0)
        p_score += (1 if test.get("expect_missing", "") in missing else 0)
    else:
        p_score = 3

    score += p_score
    details["param_score"] = p_score
    return min(score, 10), details


# ─── Benchmark runners ────────────────────────────────────────────────────────

def run_latency(model: str) -> list[dict]:
    """Tier 1: short prompt, three utterances."""
    PROBES = [
        ("simple",  "hello"),
        ("medium",  "add task buy milk"),
        ("complex", "I need to do several things: review code, fix the login bug, write release notes"),
    ]
    results = []
    print(f"  [latency] {model}", flush=True)
    for label, utterance in PROBES:
        prompt = MEDIUM_SYSTEM.replace("{USER_INPUT}", utterance)
        ttft, total, tps, _, eval_c, prompt_c, err = _post_streaming(model, prompt, timeout=300)
        row = dict(model=model, prompt_type=label, utterance=utterance,
                   ttft_ms=round(ttft) if ttft else None,
                   total_ms=round(total) if total else None,
                   tokens_per_s=round(tps, 1) if tps else None,
                   eval_tokens=eval_c, prompt_tokens=prompt_c, error=err)
        if ttft:
            print(f"    {label:8s}: TTFT={round(ttft):5d}ms  total={round(total):6d}ms  {round(tps,1):5.1f} tok/s  ({eval_c} gen tokens)", flush=True)
        else:
            print(f"    {label:8s}: ERROR — {err}", flush=True)
        results.append(row)
    return results


def run_schema(model: str) -> list[dict]:
    """Tier 2: medium prompt, 8 schema compliance tests."""
    results = []
    total_score = 0
    print(f"  [schema]  {model}", flush=True)
    for test in SCHEMA_TESTS:
        prompt = MEDIUM_SYSTEM.replace("{USER_INPUT}", test["utterance"])
        ttft, total, tps, response, _, _, err = _post_streaming(model, prompt, timeout=300)
        if ttft is None:
            print(f"    T{test['id']:02d}: ERROR — {err}", flush=True)
            results.append(dict(model=model, test_id=test["id"], utterance=test["utterance"],
                                score=0, error=err))
            continue
        score, details = score_test(test, response)
        total_score += score
        a_ok  = "Y" if details.get("action_correct") else "N"
        j_ok  = "Y" if details.get("valid_json")     else "N"
        got   = details.get("actionName", "?")
        print(f"    T{test['id']:02d}: {score:2d}/10  JSON={j_ok} action={a_ok}  got={got!r:<28}  {round(total)}ms", flush=True)
        results.append(dict(model=model, test_id=test["id"], utterance=test["utterance"],
                            score=score, details=details, total_ms=round(total)))
    pct = total_score / (10 * len(SCHEMA_TESTS)) * 100
    print(f"    TOTAL: {total_score}/{10*len(SCHEMA_TESTS)}  ({pct:.0f}%)", flush=True)
    return results


def run_context_probe(model: str) -> dict:
    """Tier 3: full system.txt prompt, probe context handling."""
    print(f"  [context] {model}")
    result = dict(model=model, probes=[])
    try:
        full_prompt = load_full_system_prompt("add task buy milk")
    except Exception as exc:
        print(f"    SKIP — could not load system.txt: {exc}")
        return result

    approx_chars  = len(full_prompt)
    approx_tokens = approx_chars // 4
    print(f"    Full prompt: ~{approx_chars} chars / ~{approx_tokens} tokens")

    ttft, total, tps, response, eval_c, prompt_c, err = _post_streaming(
        model, full_prompt, timeout=300, num_ctx=16384)

    probe: dict = dict(approx_chars=approx_chars, approx_tokens=approx_tokens,
                       actual_prompt_tokens=prompt_c,
                       ttft_ms=round(ttft) if ttft else None,
                       total_ms=round(total) if total else None,
                       error=err)

    if ttft:
        try:
            parsed = json.loads(_extract_json(response))
            probe["valid_json"] = True
            probe["action"]     = parsed.get("actionName")
        except Exception:
            probe["valid_json"] = False
            probe["raw_excerpt"] = response[:200]
        status = "OK" if probe.get("valid_json") else "BAD_JSON"
        print(f"    {status}  TTFT={round(ttft)}ms  total={round(total)}ms  prompt_tokens={prompt_c}")
    else:
        probe["valid_json"] = False
        print(f"    ERROR — {err}")

    result["probes"].append(probe)
    return result


# ─── Main ─────────────────────────────────────────────────────────────────────

def main():
    print("=" * 70)
    print("ENH-23 Phase 1 — CP Interpreter Model Benchmark")
    print(f"Started: {datetime.datetime.now().isoformat()}")
    print("=" * 70)

    all_latency = []
    all_schema  = []
    all_context = []

    SLOW_THRESHOLD_MS = 0  # 0 = skip context probe for all models (CPU-only: phi3:mini already proved 5917-token prompt times out)

    for model in MODELS_TO_TEST:
        print(f"\n{'='*60}")
        print(f"MODEL: {model}")
        print(f"{'='*60}")
        sys.stdout.flush()

        lat    = run_latency(model)
        schema = run_schema(model)
        all_latency.extend(lat)
        all_schema.extend(schema)

        complex_row = next((r for r in lat if r["prompt_type"] == "complex"), None)
        if complex_row and complex_row.get("total_ms") and complex_row["total_ms"] < SLOW_THRESHOLD_MS:
            ctx = run_context_probe(model)
            all_context.append(ctx)
        else:
            reason = "ERROR" if (complex_row and complex_row.get("error")) else "TOO SLOW"
            print(f"  [context] SKIPPED ({reason})")

        sys.stdout.flush()

    # ─── Summary table ─────────────────────────────────────────────────────────

    print("\n\n" + "=" * 70)
    print("LATENCY SUMMARY (medium prompt, representative CP utterances)")
    print("=" * 70)
    print(f"{'Model':<30} {'Type':<10} {'TTFT':>8} {'Total':>9} {'tok/s':>8}")
    print("-" * 70)
    for r in all_latency:
        ttft  = f"{r['ttft_ms']}ms"  if r.get("ttft_ms")  else "ERR"
        total = f"{r['total_ms']}ms" if r.get("total_ms") else "ERR"
        tps   = str(r.get("tokens_per_s", "-"))
        print(f"{r['model']:<30} {r['prompt_type']:<10} {ttft:>8} {total:>9} {tps:>8}")

    print("\n\n" + "=" * 70)
    print("SCHEMA COMPLIANCE SUMMARY")
    print("=" * 70)
    model_totals = {}
    for r in all_schema:
        m = r["model"]
        if m not in model_totals:
            model_totals[m] = {"score": 0, "count": 0}
        model_totals[m]["score"] += r.get("score", 0)
        model_totals[m]["count"] += 1

    print(f"{'Model':<30} {'Score':>10} {'Pct':>6}")
    print("-" * 50)
    for model, vals in model_totals.items():
        sc  = vals["score"]
        mx  = vals["count"] * 10
        pct = sc / mx * 100 if mx > 0 else 0
        print(f"{model:<30} {sc:>4}/{mx:<5} {pct:>5.0f}%")

    print("\n\n" + "=" * 70)
    print("CONTEXT PROBE SUMMARY")
    print("=" * 70)
    for ctx in all_context:
        m = ctx["model"]
        for probe in ctx["probes"]:
            ok  = probe.get("valid_json", False)
            err = probe.get("error", "")
            print(f"  {m:<30} ~{probe.get('approx_tokens',0):5d} tok: "
                  f"{'OK' if ok else ('ERR: '+str(err)[:40] if err else 'BAD_JSON')}"
                  f"  TTFT={probe.get('ttft_ms','?')}ms")

    # ─── Save results ──────────────────────────────────────────────────────────

    results = {
        "timestamp":    datetime.datetime.now().isoformat(),
        "latency":      all_latency,
        "schema":       all_schema,
        "context":      all_context,
        "model_totals": model_totals,
    }
    out_path = r"enh23_benchmark_results.json"
    with open(out_path, "w", encoding="utf-8") as fh:
        json.dump(results, fh, indent=2)
    print(f"\n\nRaw results saved to: {out_path}")
    print(f"Finished: {datetime.datetime.now().isoformat()}")


if __name__ == "__main__":
    main()
