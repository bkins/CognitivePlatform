namespace CognitivePlatform.Api.Domains.DailyRecord;

public interface IDailyRecordCommandParser
{
    ParsedDailyCommand Parse(string input);
}
