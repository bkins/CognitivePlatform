using System;
using System.IO;

namespace CognitivePlatform.Admin.Services;

public interface IPowerShellResolver
{
    string ResolvePwsh();
    string? FindPwsh7();
}

public class PowerShellResolver : IPowerShellResolver
{
    public string ResolvePwsh()
    {
        var ps7 = FindPwsh7();
        if (ps7 is not null) return ps7;

        const string ps51 = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe";
        if (File.Exists(ps51)) return ps51;

        return "powershell";
    }

    public string? FindPwsh7()
    {
        string[] candidates =
        [
            @"C:\Program Files\PowerShell\7\pwsh.exe"
          , @"C:\Program Files\PowerShell\pwsh.exe"
          , Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                       , @"Microsoft\WindowsApps\pwsh.exe")
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "pwsh.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* skip invalid PATH elements */ }
        }

        return null;
    }
}
