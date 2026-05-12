using System.Text;

namespace CognitivePlatform.Api.Models.SystemInfo;

public sealed class SystemVersionInfo
{
    public string   Application          { get; init; } = default!;
    public string?  Version              { get; init; }
    public string?  InformationalVersion { get; init; }
    public string?  CommitSha            { get; init; }
    public string   BuildConfiguration   { get; init; } = default!;
    public DateTime? BuildTimeUtc         { get; init; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"# {nameof(SystemVersionInfo)}");
        sb.AppendLine($"- **{nameof(Application)}**:\t{Application}");
        sb.AppendLine($"- **{nameof(Version)}**:\t{Version}");
        sb.AppendLine($"- **{nameof(InformationalVersion)}**:\t{InformationalVersion}");
        sb.AppendLine($"- **{nameof(BuildConfiguration)}**:\t{BuildConfiguration}");
        sb.Append($"- **{nameof(BuildTimeUtc)}**:\t\t{BuildTimeUtc?.ToLocalTime()}");

        return sb.ToString();
    }
}