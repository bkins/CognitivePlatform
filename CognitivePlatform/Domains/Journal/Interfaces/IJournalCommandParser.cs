namespace CognitivePlatform.Api.Domains.Journal.Interfaces;

public interface IJournalCommandParser
{
    ParsedJournalCommand Parse(string input);
}
