using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Journal;

namespace CognitivePlatform.Tests;

public sealed class JournalSubmissionStoreTests
{
    [Fact]
    public void SubmitJournal_AfterStoreReopens_ReplaysReceiptAndRejectsDifferentSnapshot()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var connectionString = $"Data Source={Path.Combine(directory.FullName, "journal.db")};Pooling=False";
            var store            = new SqliteObjectStore(connectionString);
            var submissionId     = Guid.NewGuid();
            var request          = new JournalSubmissionRequest(DateTimeOffset.Parse("2026-10-02T09:12:00-07:00"), "Exact text\n");

            var receipt  = store.SubmitJournal(submissionId, request);
            var reopened = new SqliteObjectStore(connectionString);
            var replay   = reopened.SubmitJournal(submissionId, request);
            var conflict = reopened.SubmitJournal(submissionId, request with { Text = "Different text" });

            Assert.NotNull(receipt);
            Assert.Equal(receipt, replay);
            Assert.Null(conflict);
            var entry    = reopened.Get<JournalEntry>(receipt.EntryId.ToString("N"));
            var revision = Assert.Single(new JournalRevisionRepository(reopened).GetRevisionsByEntryId(receipt.EntryId.ToString("N")));
            Assert.NotNull(entry);
            Assert.Equal(request.AuthoredAt, entry.CreatedUtc);
            Assert.Equal(request.Text, revision.Text);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
