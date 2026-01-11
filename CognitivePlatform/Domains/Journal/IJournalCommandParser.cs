namespace CognitivePlatform.Api.Domains.Journal;

public interface IJournalCommandParser
{
    ParsedJournalCommand Parse(string input);
}
