namespace CognitivePlatform.Admin.Services;

public enum TerminalLineKind
{
  Normal
, Error
, Warning
, Pass
, Fail
, PassSummary
, FailSummary
}

public sealed record TerminalLine(string Text, TerminalLineKind Kind = TerminalLineKind.Normal);
