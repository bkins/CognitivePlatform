using System.Text;

namespace CognitivePlatform.Api.Models.SystemInfo;

public sealed class SystemVersionInfo
{
    public string   Application          { get; init; } = default!;
    public string?  Version              { get; init; }
    public string?  InformationalVersion { get; init; }
    public string?  CommitSha            { get; init; }
    public string   BuildConfiguration   { get; init; } = default!;
    public DateTime BuildTimeUtc         { get; init; }

    public override string ToString()
    {
        var sb = new StringBuilder();

        sb.AppendLine(nameof(SystemVersionInfo));
        sb.AppendLine($"\t{nameof(Application)}:          {Application}");
        sb.AppendLine($"\t{nameof(Version)}:              {Version}");
        sb.AppendLine($"\t{nameof(InformationalVersion)}: {InformationalVersion}");
        sb.AppendLine($"\t{nameof(BuildConfiguration)}:   {BuildConfiguration}");
        sb.Append($"\t{nameof(BuildTimeUtc)}:             {BuildTimeUtc.ToLocalTime()}");

        return sb.ToString();
    }
}