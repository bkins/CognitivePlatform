using CP.Shared.Primitives.Avails;
using System.Text.RegularExpressions;
using System.Globalization;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Admin.Services;

/// <summary>
/// A single model's result row from one evaluator run.
/// </summary>
public sealed record EvalModelRow
{
    public DateTime RunDate       { get; init; }
    public string   Model         { get; init; } = string.Empty;
    public double   AvgScore      { get; init; }
    public int      Tests         { get; init; }
    public int      JsonFails     { get; init; }
    public int      IntentFails   { get; init; }
    public int      ParamFails    { get; init; }
    public int      Timeouts      { get; init; }
    public double   TokPerSecond  { get; init; }
    public string   Configuration { get; init; } = string.Empty;
}

/// <summary>
/// Parses <c>eval-*.txt</c> result files written by Run-InterpreterEval.ps1.
/// </summary>
public static class EvalResultParser
{
    private const string EvalResultsDir = @"C:\CP\Data\EvalResults";
    private static readonly Regex AnsiEscapeCodeRegex = new(RegexMatchingPatterns.AnsiEscapeCodePattern
                                                          , RegexOptions.Compiled
    );
    
    /// <summary>
    /// Reads all <c>eval-*.txt</c> files from the EvalResults directory and
    /// returns every model row found, sorted newest-run first.
    /// </summary>
    public static IReadOnlyList<EvalModelRow> LoadAll( string? dir = null )
    {
        var baseDir = dir ?? EvalResultsDir;

        if (!Directory.Exists(baseDir))
            return [];

        var rows = new List<EvalModelRow>();
        var evalFiles = Directory.EnumerateFiles(baseDir
                                               , "eval-*.txt").OrderBy(path => path);
        foreach (var file in evalFiles)
        {
            // if (Path.GetFileNameWithoutExtension(file).Length != "eval-2026-06-06 16-43-29".Length) //eval-2026-06-06 16-43-29.txt
            // {
            //     continue;
            // }

            try
            {
                rows.AddRange(ParseFile(file));
            }
            catch
            {
                // Skip unparseable files rather than crashing the UI
            }
        }

        // Most-recent run first
        rows.Sort(( rowA
                  , rowB ) => rowB.RunDate.CompareTo(rowA.RunDate));

        return rows;
    }

    // ── Parser ────────────────────────────────────────────────────────────────────

    private static IEnumerable<EvalModelRow> ParseFile( string path )
    {
        var lines = File.ReadAllLines(path);
        var (runDate, configuration) = ExtractDateConfiguration(lines
                                                              , path);
        var rows = new List<EvalModelRow>();

        var inSummary      = false;
        var headerSeen     = false;
        var separatorCount = 0;

        foreach (var line in lines)
        {
            var cleanLine = StripConsoleMarkup(line);

            if (cleanLine.Contains("FINAL SUMMARY"
                                 , StringComparison.OrdinalIgnoreCase))
                    //if (cleanLine.Contains("MODEL SUMMARY", StringComparison.OrdinalIgnoreCase))
            {
                inSummary = true;
                continue;
            }

            if (!inSummary)
                continue;

            if (cleanLine.StartsWith("---"))
            {
                separatorCount++;

                // First separator is after the "MODEL SUMMARY" heading,
                // second is after the header row — data follows after the second.
                if (separatorCount == 2)
                    headerSeen = true;

                continue;
            }

            if (cleanLine.StartsWith("==="))
                break; // end of summary block

            if (!headerSeen || string.IsNullOrWhiteSpace(cleanLine))
                continue;

            var row = TryParseModelRow(cleanLine
                                     , runDate);
            if (row is not null)
                rows.Add(row);
        }

        if (rows.Count > 0)
            return rows;

        return ParseResultSummaryRows(lines
                                    , runDate
                                    , configuration);
    }

    private static EvalModelRow? TryParseModelRow( string   line
                                                 , DateTime runDate )
    {
        // Data rows are space-separated with 9 tokens:
        // Model  AvgScore  Tests  JsonFails  IntentFails  ParamFails  FailTypeCount  Timeouts  Tok/s
        var parts = line.Split(' '
                             , StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 9)
            return null;

        if (!double.TryParse(parts[1]
                           , NumberStyles.Float
                           , CultureInfo.InvariantCulture
                           , out var avgScore))
            return null;

        int.TryParse(parts[2]
                   , out var tests);
        int.TryParse(parts[3]
                   , out var jsonFails);
        int.TryParse(parts[4]
                   , out var intentFails);
        int.TryParse(parts[5]
                   , out var paramFails);
        int.TryParse(parts[7]
                   , out var timeouts);
        double.TryParse(parts[8]
                      , NumberStyles.Float
                      , CultureInfo.InvariantCulture
                      , out var tokPerSec);

        return new EvalModelRow
               {
                       RunDate      = runDate
                     , Model        = parts[0]
                     , AvgScore     = avgScore
                     , Tests        = tests
                     , JsonFails    = jsonFails
                     , IntentFails  = intentFails
                     , ParamFails   = paramFails
                     , Timeouts     = timeouts
                     , TokPerSecond = tokPerSec
               };
    }

    private static IReadOnlyList<EvalModelRow> ParseResultSummaryRows( string[] lines
                                                                     , DateTime runDate
                                                                     , string   config )
    {
        var aggregates = new Dictionary<string, ModelAggregate>(StringComparer.OrdinalIgnoreCase);

        ResultBlock? current             = null;
        double?      pendingTokPerSecond = null;

        foreach (var rawLine in lines)
        {
            var line = StripConsoleMarkup(rawLine);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var tokMatch = Regex.Match(line
                                     , RegexMatchingPatterns.TokenSpeedPattern
                                     , RegexOptions.IgnoreCase);
            if (tokMatch.Success
             && double.TryParse(tokMatch.Groups["tok"].Value
                              , NumberStyles.Float
                              , CultureInfo.InvariantCulture
                              , out var tokPerSecond))
            {
                pendingTokPerSecond = tokPerSecond;
            }

            if (line.Contains("RESULT SUMMARY"
                            , StringComparison.OrdinalIgnoreCase))
            {
                AddResultBlock(aggregates
                             , current);
                current             = new ResultBlock { TokPerSecond = pendingTokPerSecond };
                pendingTokPerSecond = null;
                continue;
            }

            if (current is null)
                continue;

            if (TryReadValue(line
                           , "Model"
                           , out var model))
            {
                current.Model = model;
                continue;
            }

            if (TryReadValue(line
                           , "Score"
                           , out var scoreText)
             && double.TryParse(scoreText
                              , NumberStyles.Float
                              , CultureInfo.InvariantCulture
                              , out var score))
            {
                current.Score = score;
                continue;
            }

            if (TryReadValue(line
                           , "Parsed"
                           , out var parsed))
            {
                current.Parsed = parsed.Equals("Yes"
                                             , StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (TryReadValue(line
                           , "Action Correct"
                           , out var actionCorrect))
            {
                current.ActionCorrect = actionCorrect.Equals("Yes"
                                                           , StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (TryReadValue(line
                           , "Parameters Correct"
                           , out var parametersCorrect))
            {
                current.ParametersCorrect = parametersCorrect.Equals("Yes"
                                                                   , StringComparison.OrdinalIgnoreCase);
                continue;
            }
        }

        AddResultBlock(aggregates
                     , current);

        var result = aggregates.Select(pair => pair.Value.ToRow(runDate
                                                              , config
                                                              , pair.Key))
                               .OrderByDescending(row => row.RunDate)
                               .ThenBy(row => row.Model)
                               .ToList();

        return result;
    }

    private static void AddResultBlock( Dictionary<string, ModelAggregate> aggregates
                                      , ResultBlock?                       block )
    {
        if (block is null
         || string.IsNullOrWhiteSpace(block.Model)
         || block.Score is null)
            return;

        if (!aggregates.TryGetValue(block.Model
                                  , out var aggregate))
        {
            aggregate               = new ModelAggregate();
            aggregates[block.Model] = aggregate;
        }

        aggregate.Tests++;
        aggregate.ScoreTotal += block.Score.Value;

        if (block.Parsed == false)
            aggregate.JsonFails++;

        if (block.ActionCorrect == false)
            aggregate.IntentFails++;

        if (block.ParametersCorrect == false)
            aggregate.ParamFails++;

        if (block.TokPerSecond is not null)
        {
            aggregate.TokPerSecondTotal += block.TokPerSecond.Value;
            aggregate.TokPerSecondCount++;
        }
    }

    private static bool TryReadValue( string     line
                                    , string     key
                                    , out string value )
    {
        value = string.Empty;

        var match = Regex.Match(line
                              , RegexMatchingPatterns.CreateDynamicKeyLookupPattern(key)
                              , RegexOptions.IgnoreCase);
        
        if (match.Success.Not()) return false;

        value = match.Groups["value"].Value.Trim();
        
        return value.Length > 0;
    }
    
    private static string StripConsoleMarkup(string line)
    {
        // Using the compiled instance is significantly faster in high-throughput loops
        var withoutAnsi = AnsiEscapeCodeRegex.Replace(line, string.Empty);
    
        var chars = withoutAnsi.Select(ch => char.IsControl(ch) || IsBoxDrawing(ch) ? ' ' : ch)
                               .ToArray();
                           
        return new string(chars).Trim();
    }
    
    // private static string StripConsoleMarkup( string line )
    // {
    //     var withoutAnsi = Regex.Replace(line
    //                                   , @"\x1B\[[0-9;]*[A-Za-z]"
    //                                   , string.Empty);
    //
    //     var chars = withoutAnsi.Select(ch => char.IsControl(ch) || IsBoxDrawing(ch)
    //                                                  ? ' '
    //                                                  : ch)
    //                            .ToArray();
    //
    //     return new string(chars).Trim();
    // }

    private static bool IsBoxDrawing( char ch ) => ch is >= '\u2500' and <= '\u257F';

    private sealed class ResultBlock
    {
        public string? Model             { get; set; }
        public double? Score             { get; set; }
        public bool?   Parsed            { get; set; }
        public bool?   ActionCorrect     { get; set; }
        public bool?   ParametersCorrect { get; set; }
        public double? TokPerSecond      { get; set; }
    }

    private sealed class ModelAggregate
    {
        public int    Tests;
        public double ScoreTotal;
        public int    JsonFails;
        public int    IntentFails;
        public int    ParamFails;
        public double TokPerSecondTotal;
        public int    TokPerSecondCount;

        public EvalModelRow ToRow( DateTime runDate
                                 , string   config
                                 , string   model )
        {
            return new EvalModelRow
                   {
                           RunDate = runDate
                         , Model   = model
                         , AvgScore = Tests == 0
                                              ? 0
                                              : ScoreTotal / Tests
                         , Tests       = Tests
                         , JsonFails   = JsonFails
                         , IntentFails = IntentFails
                         , ParamFails  = ParamFails
                         , Timeouts    = 0
                         , TokPerSecond = TokPerSecondCount == 0
                                                  ? 0
                                                  : TokPerSecondTotal / TokPerSecondCount
                         , Configuration = config
                   };
        }
    }

    private static (DateTime DateGenerated, string Configuration) ExtractDateConfiguration( string[] lines
                                                                                          , string   filePath )
    {
        DateTime? dateGenerated = null;
        var       configuration = string.Empty;

        foreach (var line in lines)
        {
            if (line.StartsWith("Generated"
                              , StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex < 0)
                    continue;

                // Strip the literal " UTC" suffix produced by the eval runner before parsing.
                var dateStr = line[(colonIndex + 1)..].Trim()
                                                      .Replace(" UTC"
                                                             , string.Empty
                                                             , StringComparison.OrdinalIgnoreCase);

                if (DateTime.TryParseExact(dateStr
                                         , "yyyy-MM-dd HH:mm:ss"
                                         , CultureInfo.InvariantCulture
                                         , DateTimeStyles.AssumeUniversal
                                         , out var parsed))
                    dateGenerated = parsed.ToLocalTime();
            }

            if (line.StartsWith("Configuration"
                              , StringComparison.OrdinalIgnoreCase))
            {
                var colonIndex = line.IndexOf(':');
                if (colonIndex < 0)
                    continue;

                configuration = line[(colonIndex + 1)..].Trim();

            }

            if (dateGenerated.HasValue
             && !string.IsNullOrWhiteSpace(configuration))
            {
                return (dateGenerated.Value, configuration);
            }
        }

        // // Fall back to the date embedded in the file name (eval-yyyy-MM-dd.txt)
        // var fileName = Path.GetFileNameWithoutExtension(filePath);
        // if (fileName.Length >= 15
        //  && DateTime.TryParseExact(fileName[5..]
        //                          , "yyyy-MM-dd"
        //                          , System.Globalization.CultureInfo.InvariantCulture
        //                          , System.Globalization.DateTimeStyles.None
        //                          , out var fileDate))
        //     dateGenerated = fileDate;

        return (File.GetLastWriteTime(filePath), "Debug - no configuration info found");
    }
}
