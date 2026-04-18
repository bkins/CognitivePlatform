# Standards — Cognitive Platform

_Coding style, naming conventions, and development discipline for all CP universe repos._

---

# Naming

## General / global naming conventions

### 1. All acronyms are treated and cased like any other word.
   - `NL` → `NaturalLanguage`, `HTTP` → `Http`, `CP` → `Cp` in compound names

### 2. Single-letter words (e.g. "A", "I") should be avoided in identifiers.
   - `BuildAClass` looks awkward. Prefer `BuildClass`.

### 3. Avoid acronyms except when the full name would be extremely long.
   - Good judgement required. When in doubt, spell it out.

---

# Folder organization

### 1. Do not create `Interfaces/` subfolders unless a folder contains more than 3–4 interfaces.
### 2. Keep interfaces and implementations together during early phases.
### 3. Split into dedicated interface folders only when the module becomes large (5+ files).

---

# Coding style

### 1. Object initializers use leading commas

```csharp
return new ActionMetadata
       {
           Name        = methodInfo.Name
         , Description = attribute.Description
         , Examples    = attribute.Examples
       };
```

### 2. Columns are vertically aligned
- Align property names and assignment operators.
- Align method parameters when they break across multiple lines.

### 3. Long parameter lists use one parameter per line
- With alignment to improve scanning in Rider.

### 4. Expression-bodied members
- Allowed only when the body is trivial and short.
- Otherwise prefer full statement bodies.

### 5. Prefer `var` for obvious types
- Use `var` when the type is clear from the right-hand side.
- Use explicit types when clarity improves (rare).

### 6. Acronyms are treated as normal words
- `NL` → `NaturalLanguage`, `HTTP` → `Http`
- File names follow PascalCase with acronym normalization.

### 7. Interfaces placed with implementations unless module has 4+ interfaces
- Do not create `Interfaces/` subfolders until the module grows large.

### 8. Always prefer leading commas in enum and initializer lists

```csharp
public enum ThingState
{
    Unknown
  , Starting
  , Running
  , Failed
}
```

### 9. Lambda and LINQ: no single-character variable names (except simple counts)

Preferred:
```csharp
var sprintEndDateById = iterations
    .SelectMany(iteration => iteration.IterationIds.Select(id => (id, iteration.EndDate)))
    .ToDictionary(sprintInfo => sprintInfo.id
                , sprintInfo => sprintInfo.EndDate);
```

Not preferred:
```csharp
var sprintEndDateById = iterations.SelectMany(i => i.IterationIds.Select(id => (id, i.EndDate)))
                                  .ToDictionary(x => x.id, x => x.EndDate);
```

---

# Development discipline

## Definition of Done

A workstream is not complete until all of the following are true:

1. **Feature works** — manual UI test or automated test passes.
2. **BACKLOG.md updated** — any out-of-scope bugs or ideas found during the work are
   captured with an ID.
   1. If items are completed logged in this document, then update this document to reflect that the item is done

3. **ROADMAP.md updated** — the completed item is marked done; any new planned items
   are added with a status.
4. **If a new architectural pattern was introduced**, a sentence is added to
   `ARCHITECTURE.md` explaining it.

This is two minutes of doc work per workstream, not a ceremony.

## Scope discipline

When a bug or enhancement is discovered during active development on a different feature:
- **Do not fix it in-place.** Add it to `BACKLOG.md` with an ID.
- Finish the active workstream first.
- This keeps sessions focused and avoids "while I'm in here..." drift.
