using System.Text;

namespace CognitivePlatform.Api.Models.SystemInfo;

public sealed class SystemEnvironmentInfo
{
        public string   EnvironmentName { get; init; } = default!;
        public string   MachineName     { get; init; } = default!;
        public string   ContentRoot     { get; init; } = default!;
        public string   DataRoot        { get; init; } = default!;
        public string   DatabasePath    { get; init; } = default!;
        public int      ProcessId       { get; init; }
        public DateTime StartedAtUtc    { get; init; }

        public override string ToString()
        {
                var sb = new StringBuilder();

                sb.AppendLine(nameof(SystemEnvironmentInfo));
                sb.AppendLine($"\t{nameof(EnvironmentName)}:  {EnvironmentName}");
                sb.AppendLine($"\t{nameof(MachineName)}:      {MachineName}");
                // sb.AppendLine($"\t{nameof(ContentRoot)}:      {ContentRoot}");
                // sb.AppendLine($"\t{nameof(DataRoot)}:         {DataRoot}");
                sb.AppendLine($"\t{nameof(DatabasePath)}:     {DatabasePath}");
                sb.AppendLine($"\t{nameof(ProcessId)}:        {ProcessId}");
                sb.Append($"\t{nameof(StartedAtUtc)}:     {StartedAtUtc.ToLocalTime()}");

                return sb.ToString();
        }
}