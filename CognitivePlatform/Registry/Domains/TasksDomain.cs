namespace CognitivePlatform.Api.Registry.Domains;

public sealed record TasksDomain : IDomainDefinition
{
    public string Name        => "Tasks";
    public string Description => "To-do list with Eisenhower-matrix priority awareness.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "task"
      , "tasks"
      , "todo"
      , "to-do"
      , "complete"
      , "done"
      , "priority"
      , "urgent"
      , "due"
      , "deadline"
      , "finish"
      , "add task"
    };
}
