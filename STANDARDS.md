# Standards — CognitivePlatform

_Coding style, naming conventions, and development discipline for the CognitivePlatform repository._

---

# Naming

## General / global naming conventions

### 1. All acronyms are treated and cased like any other word.
   - `NL` → `NaturalLanguage`, `HTTP` → `Http` in compound names

### 2. Single-letter words (e.g. "A", "I") should be avoided in identifiers.
   - `BuildAClass` looks awkward. Prefer `BuildClass`.

### 3. Avoid acronyms except when the full name would be extremely long.
   - Good judgement required. When in doubt, spell it out.

---

# Folder organization

### 1. Do not create `Interfaces/` subfolders unless a folder contains more than 3–4 interfaces.
### 2. Keep interfaces and implementations together during early phases. In the same folder
### 3. Split into dedicated interface folders only when the module becomes large (5+ files).

### 4. Avoid having more than one class/type per file

- enums can be in the same file as the class that uses it, only if it is only used by that class. If the enum is used outside of the class then it should go in its own file

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
- With alignment to improve scanning in Rider / Visual Studio.
#### 3.1 Long dot chaining:
```csharp
var temp = myObject.FirstMethod()
                   .NextMethod()
                   .AnotherMethod();
```
### 4. Expression-bodied members
- Allowed only when the body is trivial and short.
- Otherwise prefer full statement bodies.

### 5. Prefer `var` for obvious types
- Use `var` when the type is clear from the right-hand side.
- Use explicit types when clarity improves.

### 6. Acronyms are treated as normal words
- `NL` → `NaturalLanguage`, `HTTP` → `Http`
- File names follow PascalCase with acronym normalization.

### 7. Interfaces placed with implementations unless module has 3-4+ interfaces
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

### 9. Lambda and LINQ: no single-character variable names (except simple counts). Also note how the '.' line up, leading commas

Preferred:
```csharp
var sprintEndDateById = iterations.SelectMany(iteration => iteration.IterationIds
                                                                    .Select(id => (id
                                                                                  , iteration.EndDate)))
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
2. **No compilation warnings or errors** exist.
3. **Git Commits & Push** — descriptive commit messages, remote repository is up to date.

## Scope discipline

When a bug or enhancement is discovered during active development on a different feature:
- **Do not fix it in-place.** Log it to the backlog or track it as an issue.
- Finish the active workstream first.
- This keeps sessions focused and avoids "while I'm in here..." drift.

---

**Assumptions**

> Never make assumption of what code looks like, unless there is no way to see into that file/library. I am always happy to show you code or grant you access. Just ask me if you don't have access.
