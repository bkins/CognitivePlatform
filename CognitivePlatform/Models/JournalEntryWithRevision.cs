using CognitivePlatform.Api.Domains.Journal;

namespace CognitivePlatform.Api.Models;

public sealed record JournalEntryWithRevision
(
    JournalEntry    Entry
  , JournalRevision LatestRevision
  , bool            IsEdited
);

