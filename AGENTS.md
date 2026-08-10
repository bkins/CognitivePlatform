# AGENTS.md — CognitivePlatform

This file is read by Antigravity at the start of every session. Follow every convention here unless explicitly overridden in the current session.

---

## Coding Style & Standards

These styling rules apply to all C# and .NET code written in this repository.

### 1. Object Initializers
Always use leading commas and vertically align columns (property names, assignment operators, values).
```csharp
var metadata = new ActionMetadata
               {
                   Name        = methodInfo.Name
                 , Description = attribute.Description
                 , Examples    = attribute.Examples
               };
```

### 2. Enums
Always use leading commas in enum lists.
```csharp
public enum ProcessState
{
    Unknown
  , Starting
  , Running
  , Failed
}
```

### 3. Lambda & LINQ Variable Naming
Do not use single-character variable names in lambda expressions or LINQ queries (except for simple count variables like `i` or `j` in local index contexts).
```csharp
// Preferred
var activeItems = items.Where(item => item.IsActive)
                       .Select(item => item.Id);

// Avoid
var activeItems = items.Where(x => x.IsActive)
                       .Select(i => i.Id);
```

### 4. Expression-Bodied Members
Use expression-bodied members only when the body is trivial, simple, and short. Otherwise, prefer full statement bodies.

### 5. Type Inference (`var`)
Prefer `var` when the type is obvious from the right-hand side of the assignment. Use explicit types when it improves code readability.

### 6. Acronyms & Casing
Treat acronyms as normal words. Normal casing should be used.
* Good: `Http`, `NaturalLanguage`, `Cp`, `Sqlite`
* Avoid: `HTTP`, `NL`, `CP`, `SQLITE`

### 7. Folder Organization & Interfaces
* Do not create `Interfaces/` subfolders unless a module/folder has more than 3-4 interfaces.
* Keep interfaces and implementations in the same directory.
* Avoid having more than one class, struct, or interface type per file.
* Enums should be in their own file unless they are strictly used within a single class and not exposed externally.

---

## Naming Conventions

* Avoid single-letter words in identifiers (e.g., use `BuildClass` instead of `BuildAClass`).
* Spell out acronyms unless the full identifier name would become excessively long.
* Test classes must be named `<ClassUnderTest>Tests` (e.g., `TaskServiceTests`).
* Test method names should use underscores as word separators to clearly describe the scenario: `MethodOrBehavior_Condition_ExpectedResult` (e.g., `Create_AssignsId_WhenIdIsEmpty`).

---

## Testing Standards

### 1. Testing Stack
* **Framework**: xUnit
* **Mocking**: Moq
* **Assertions**: xUnit built-in Assert (avoid FluentAssertions unless requested)

### 2. Arrange-Act-Assert (AAA) Layout
Always structure unit tests clearly using AAA. Separate the phases with blank lines. Do not add comments like `// Arrange`, `// Act`, or `// Assert`.
```csharp
[Fact]
public void CalculateTotal_WithValidItems_ReturnsSum()
{
    var calculator = new InvoiceCalculator();
    var items = new[] { 10.0m, 20.0m };

    var result = calculator.Calculate(items);

    Assert.Equal(30.0m, result);
}
```

### 3. What to Test
* Public API methods, domain logic services, pure functions, parsers, scoring algorithms.
* Edge cases: null, empty, boundary, and unexpected/invalid inputs.
* Mock external dependencies (like databases, APIs, file systems) using Moq.

## Model Catalog Probing Resiliency Invariant

On Release/production builds or when `ShouldProbeModels = false` in settings, the LLM model catalog remains empty.
* Never check `modelInfo is null` as a usability failure.
* Only fail early if `modelInfo` is explicitly present in the catalog AND marked unusable (`IsUsable == false`).
* If `modelInfo` is null, proceed and let the LLM router handle execution and its configured fallbacks.

---

## Definition of Done (DoD)

A task or feature is not complete until:
1. The code builds with zero compilation errors and warnings.
2. New or modified logic is covered by at least one unit test.
3. All unit and regression tests pass successfully.
4. **Git Commits & Push**: Commit changes with clear, descriptive commit messages, and push to the remote repository.
5. **Production Data Protection**: Production data (located in `C:\CP\Data\Prod\*`) is sacred. Never run destructive or modifying commands against production data. If an operation is close to modifying production databases or folders, stop and request user permission first.
