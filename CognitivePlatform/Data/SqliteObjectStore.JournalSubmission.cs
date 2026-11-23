using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CognitivePlatform.Api.Domains.Journal;
using Microsoft.Data.Sqlite;

namespace CognitivePlatform.Api.Data;

public partial class SqliteObjectStore
{
    public JournalSubmissionReceipt? SubmitJournal(Guid submissionId, JournalSubmissionRequest request)
    {
        var receiptKey = $"journal-submission:{submissionId:N}";
        var textHash   = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Text)));
        var authoredAt = request.AuthoredAt.ToUniversalTime();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: false);

        var existing = ReadJournalSubmission(transaction, receiptKey);
        if (existing is not null)
        {
            return existing.Receipt.AuthoredAt == authoredAt && existing.TextHash == textHash
                ? existing.Receipt
                : null;
        }

        var receipt  = new JournalSubmissionReceipt(submissionId, Guid.NewGuid(), authoredAt, DateTimeOffset.UtcNow);
        var entry    = new JournalEntry
                       {
                           Id         = receipt.EntryId.ToString("N")
                         , CreatedUtc = authoredAt
                       };
        var revision = new JournalRevision
                       {
                           RevisionId = Guid.NewGuid().ToString("N")
                         , EntryId    = entry.Id
                         , CreatedUtc = authoredAt
                         , Text       = request.Text
                         , State      = JournalEntryState.Committed
                       };

        InsertJournalObject(transaction, entry.Id, entry, authoredAt, receipt.SubmittedAt);
        InsertJournalObject(transaction, revision.RevisionId, revision, authoredAt, receipt.SubmittedAt);
        InsertJournalObject(transaction, receiptKey, new StoredJournalSubmission(receipt, textHash)
                          , receipt.SubmittedAt, receipt.SubmittedAt);

        transaction.Commit();
        return receipt;
    }

    private StoredJournalSubmission? ReadJournalSubmission(SqliteTransaction transaction, string receiptKey)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Json FROM Objects WHERE Id = $id AND Type = $type";
        command.Parameters.AddWithValue("$id", receiptKey);
        command.Parameters.AddWithValue("$type", typeof(StoredJournalSubmission).FullName!);

        return command.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<StoredJournalSubmission>(json, _jsonOptions)
            : null;
    }

    private void InsertJournalObject<T>(SqliteTransaction transaction
                                     , string            id
                                     , T                 value
                                     , DateTimeOffset    createdAt
                                     , DateTimeOffset    updatedAt)
    {
        using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO Objects (Id, Type, PartitionKey, Json, CreatedUtc, UpdatedUtc)
            VALUES ($id, $type, NULL, $json, $created, $updated)
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$type", typeof(T).FullName!);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(value, _jsonOptions));
        command.Parameters.AddWithValue("$created", createdAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", updatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }
}
