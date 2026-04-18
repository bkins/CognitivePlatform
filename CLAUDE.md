- # CLAUDE.md — Cognitive Platform (NLCS)

  This file is read by Claude Code at the start of every session. Follow every convention here unless Ben explicitly overrides it in the current session.

  ------

  ## Project Overview

  The **Cognitive Platform** (CP) is a natural-language command system backed by a .NET 10 Web API (`CognitivePlatform.Api`) and a .NET MAUI client (`LocalAIAssistant`). The system routes natural language input through a `FastPathResolver` (no LLM cost) or falls back to a Groq LLM. Domains include Tasks, Journal, and Memory. A universal `SqliteObjectStore` provides persistence.

  Key projects:

  - `CognitivePlatform.Api` — domain logic, orchestration, fast-path, actions, persistence
  - `CognitivePlatform.Tests` — **all unit tests live here**
  - `CP.Shared.Primitives` — zero-dependency extensions and utilities
  - `CP.Client.Core` — Exposes extensions and utilities in the `CP.Shared.Primitives` to clients; and for client specific extensions and utilities.
  - `LocalAIAssistant` — .NET MAUI client (MVVM, no direct test coverage today)

  ------

  ## Coding Style

  These rules mirror `STANDARDS.md` exactly. Never deviate from them in generated code.

  ### Object initializers use leading commas, columns vertically aligned

  ```csharp
  return new ActionMetadata
         {
             Name        = methodInfo.Name
           , Description = attribute.Description
           , Examples    = attribute.Examples
         };
  ```

  ### Enums use leading commas

  ```csharp
  public enum ThingState
  {
      Unknown
    , Starting
    , Running
    , Failed
  }
  ```

  ### Lambda and LINQ — no single-character variable names (except simple counts)

  ```csharp
  // Correct
  var sprintEndDateById = iterations
      .SelectMany(iteration => iteration.IterationIds.Select(id => (id, iteration.EndDate)))
      .ToDictionary(sprintInfo => sprintInfo.id
                  , sprintInfo => sprintInfo.EndDate);
  
  // Never do this
  var x = items.Where(i => i.IsActive).Select(i => i.Id);
  ```

  ### Other style rules

  - Prefer `var` when the type is obvious from the right-hand side
  - Expression-bodied members only when the body is trivial and short; otherwise full statement bodies
  - Long parameter lists: one parameter per line, aligned
  - Acronyms treated as normal words: `Http`, `NaturalLanguage`, `Cp`
  - No `Interfaces/` subfolder unless a module has 4+ interfaces

  ------

  ## Naming Conventions

  - No single-letter words in identifiers (`BuildClass` not `BuildAClass`)
  - No unnecessary acronyms — spell it out unless the name would be extremely long
  - Test method names use `_` as a word separator: `Parses_Text_Only`, `Returns_Empty_When_No_Input`
  - Test classes are named `<ClassUnderTest>Tests` (e.g. `JournalCommandParserTests`, `TaskServiceTests`)

  ------

  ## Testing Stack

  ```
  Framework:  xUnit 2.9+
  Runner:     xunit.runner.visualstudio
  Coverage:   coverlet.collector
  Mocking:    Moq  ← add to .csproj if not already present
  Assertions: xUnit Assert (built-in) — do NOT introduce FluentAssertions without asking Ben first
  ```

  > **If Moq is not yet in `CognitivePlatform.Tests.csproj`, add it before writing any mock-based test.** `<PackageReference Include="Moq" Version="4.20.72" />`

  ------

  ## Test Conventions

  These conventions are derived from the existing `JournalCommandParserTests.cs` — treat that file as the canonical example.

  ### Structure

  - One test class per class under test
  - Tests live in `src/CognitivePlatform.Tests/`
  - Mirror the source namespace loosely — no strict nesting required
  - No `[TestClass]` (that's MSTest) — use plain `public class`
  - No `[SetUp]` / `[TearDown]` — use constructor injection and `IDisposable` if teardown is needed

  ### Test method naming

  Use `MethodOrBehaviour_Condition_ExpectedResult` or a plain readable description with underscores. Be specific enough that a failing test name tells you exactly what broke.

  ```csharp
  // Good
  [Fact]
  public void Parse_ReturnsEmptyText_WhenInputIsNull() { }
  
  [Fact]
  public void Complete_SetsCompletedAt_WhenTaskExists() { }
  
  [Fact]
  public void TryResolve_ReturnsFalse_WhenInputIsEmpty() { }
  
  // Avoid
  [Fact]
  public void TestParse() { }
  ```

  ### Arrange / Act / Assert

  Always structure tests with the three phases. Use blank lines to separate them. Do **not** add `// Arrange`, `// Act`, `// Assert` comments — let the blank lines speak.

  ```csharp
  [Fact]
  public void Parses_Tags_And_Mood()
  {
      var input = """
                  Had a productive meeting.
                  Tags: "work", "planning"
                  Mood: "Optimistic"
                  """;
  
      var result = _parser.Parse(input);
  
      Assert.Equal("Had a productive meeting.",  result.Text);
      Assert.Equal(new[] { "work", "planning" }, result.Tags);
      Assert.Equal("Optimistic",                 result.Mood);
  }
  ```

  ### Assertions

  - Use `Assert.Equal`, `Assert.True`, `Assert.False`, `Assert.Null`, `Assert.NotNull`, `Assert.Empty`
  - Align assertion columns vertically when multiple assertions exist in one test (see example above)
  - Prefer one logical assertion group per test — split into multiple `[Fact]` methods if needed

  ### Mocking

  - Use Moq for all interface dependencies
  - Declare mocks as fields, initialize in constructor
  - Use `Mock<T>.Object` when passing to SUT
  - Verify interactions only when the interaction itself is the thing being tested

  ```csharp
  public class TaskServiceTests
  {
      private readonly Mock<IObjectStore> _storeMock = new();
      private readonly TaskService        _service;
  
      public TaskServiceTests()
      {
          _service = new TaskService(_storeMock.Object);
      }
  
      [Fact]
      public void Create_AssignsId_WhenIdIsEmpty()
      {
          var task = new TaskItem { Id = string.Empty };
  
          _storeMock.Setup(store => store.Save(It.IsAny<TaskItem>(), It.IsAny<string>()));
  
          var result = _service.Create(task);
  
          Assert.NotNull(result.Id);
          Assert.NotEmpty(result.Id);
      }
  }
  ```

  ### What to test

  **Always test:**

  - Public methods on domain services (`TaskService`, `JournalService`, etc.)
  - Parsers and interpreters (`JournalCommandParser`, `FastPathResolver`)
  - Pure logic methods (scoring, filtering, mapping)
  - Edge cases: null input, empty input, boundary values, unexpected formats

  **Test with care (integration-adjacent):**

  - Methods that touch `IObjectStore` — mock the store, test the logic around it

  **Do not test (out of scope for unit tests):**

  - MAUI ViewModels (UI lifecycle makes this impractical without a test harness)
  - EF/SQLite internals (`SqliteObjectStore` implementation details)
  - `Program.cs` / DI wiring

  ------

  ## Definition of Done (includes tests)

  A workstream is **not complete** until:

  1. Feature works — manual UI test or automated test passes
  2. **New or changed logic has at least one unit test**
  3. `BACKLOG.md` updated with any out-of-scope items found during the work
  4. No build warnings introduced
  5. Update `C:\Users\benho\source\Application Documentation\The CP Universe\Natural Language Command System\Developer Log.md` with what was done in this session. (see existing entries in this document for pattern of data to enter)

  ---

  ## Documentation
  Doc locations and sync protocol: see Discipline Mode.
  Vault: `C:\Users\benho\source\Application Documentation\The CP Universe\_Repo Docs\`
  Workbench: `C:\Users\benho\source\repos\CP\CP.Workbench\Documentation\CP.WorkBench\`
  
  > Ask about entering Discipline Mode when: starting a complex feature, working 
  > across multiple domains, or when meaningful work (feature, bug fix, phase) 
  > has just been completed.
  
  ------
  
  ## Soft Delete Invariant
  
  Every domain object has `IsDeleted` / `DeletedUtc`. Hard deletes are never performed. Tests that exercise delete behaviour must assert soft-delete semantics, not hard removal.
  
  ## Journal Append-Only Invariant
  
  `JournalEntry` is immutable after creation. Mutations create a new `JournalRevision`. `LatestRevision` is always the authoritative content. Never write a test that mutates an entry directly.
  
  ------
  
  ## Running Tests
  
  ```bash
  dotnet test src/CognitivePlatform.Tests/CognitivePlatform.Tests.csproj
  ```
  
  To run a single test class:
  
  ```bash
  dotnet test --filter "FullyQualifiedName~TaskServiceTests"
  ```
  
  To run with coverage:
  
  ```bash
  dotnet test --collect:"XPlat Code Coverage"
  ```

---

# Documentation -- How and what:

## Determining what to work on next & tracking what's done

Read these files:

* Primary source:
  * C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\_Documentation\CognitivePlatform\BACKLOG.md
* Other files with future work:
  * C:\Users\benho\source\repos\CognitivePlatform\BACKLOG.md
  * C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\_Documentation\CognitivePlatform\DEFERRED.md
  * C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\_Documentation\CognitivePlatform\ROADMAP.md
* Other places to search when you cannot find documentation:
  * C:\Users\benho\source\Application Documentation\The CP Universe\\*.md
  * C:\Users\benho\source\repos\CP\CP.Workbench\Documentation\CP.WorkBench\\*.md

## How the system works and definitions

Read these files:

* C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\_Documentation\CognitivePlatform\ARCHITECTURE.md
* C:\Users\benho\source\repos\CognitivePlatform\CognitivePlatform\_Documentation\CognitivePlatform\SYSTEM.md

## Discipline Mode

Read this file for what **"Discipline Mode"** is: C:\Users\benho\source\Application Documentation\The CP Universe\Natural Language Command System\Discipline Mode Guidelines for Claude.md

If Ben does not explicitly say **"Enter Discipline Mode."**, when starting a new work effort, milestone, and/or feature, ask if "Discipline Mode" should be initiated.
