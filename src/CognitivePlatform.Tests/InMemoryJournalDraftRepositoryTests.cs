using CognitivePlatform.Api.Domains.Journal;

namespace CognitivePlatform.Tests;

public class InMemoryJournalDraftRepositoryTests
{
    private readonly InMemoryJournalDraftRepository _repository = new();

    [Fact]
    public async Task AddAsync_StoresDraftSuccessfully()
    {
        var draft = new JournalDraft
                    {
                        Id         = Guid.NewGuid()
                      , Text       = "Draft text"
                      , CreatedUtc = DateTimeOffset.UtcNow
                    };

        var exception = await Record.ExceptionAsync(() => _repository.AddAsync(draft));

        Assert.Null(exception);
    }
}
