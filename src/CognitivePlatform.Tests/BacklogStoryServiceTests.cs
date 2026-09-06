using CognitivePlatform.Admin.Services;

namespace CognitivePlatform.Tests;

public sealed class BacklogStoryServiceTests
{
    [Fact]
    public async Task AddStoryAsync_ValidStory_AppendsSafeEnhancementAndCompilesBoard()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var backlogPath = Path.Combine(temporaryDirectory, "BACKLOG.original.md");
            await File.WriteAllTextAsync(backlogPath, """
                # Backlog

                ## Enhancements

                | ID | Description | Area | Status |
                | --- | --- | --- | --- |
                | ENH-4 | **Existing story** — Existing description. | Existing Area | Planned |
                """);

            var compiler = new RecordingBacklogBoardCompiler();
            var service = new BacklogStoryService(
                new BacklogStoryOptions(new Dictionary<string, string>
                {
                    ["Test Project"] = backlogPath
                })
              , compiler);

            var result = await service.AddStoryAsync(new AddBacklogStoryRequest(
                "Test Project"
              , "New | story"
              , "A line one\nline two description"
              , "Admin | Board"
              , "Planned"));

            var updatedBacklog = await File.ReadAllTextAsync(backlogPath);

            Assert.True(result.IsSuccess);
            Assert.Equal("ENH-5", result.StoryId);
            Assert.True(compiler.WasCompiled);
            Assert.Contains("| ENH-5 | **New \\| story** — A line one line two description | Admin \\| Board | Planned |", updatedBacklog);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task AddStoryAsync_UnknownProject_DoesNotWriteOrCompile()
    {
        var compiler = new RecordingBacklogBoardCompiler();
        var service = new BacklogStoryService(new BacklogStoryOptions(new Dictionary<string, string>()), compiler);

        var result = await service.AddStoryAsync(new AddBacklogStoryRequest(
            "Unknown"
          , "Story"
          , "Description"
          , "Area"
          , "Planned"));

        Assert.False(result.IsSuccess);
        Assert.False(compiler.WasCompiled);
        Assert.Equal("The selected project is not configured for story creation.", result.ErrorMessage);
    }

    private sealed class RecordingBacklogBoardCompiler : IBacklogBoardCompiler
    {
        public bool WasCompiled { get; private set; }

        public Task CompileAsync(CancellationToken cancellationToken = default)
        {
            WasCompiled = true;
            return Task.CompletedTask;
        }
    }
}
