using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Integrations.FileSync;
using CognitivePlatform.Api.Integrations.FileSync.Models;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.FileSync;

[Domain(typeof(FileSyncDomain))]
public class FileSyncActions
{
    private readonly IFileSyncProvider _phoneProvider;
    private readonly IFileSyncService  _syncService;

    public FileSyncActions(IFileSyncProvider phoneProvider, IFileSyncService syncService)
    {
        _phoneProvider = phoneProvider ?? throw new ArgumentNullException(nameof(phoneProvider));
        _syncService   = syncService   ?? throw new ArgumentNullException(nameof(syncService));
    }

    // -----------------------------------------------------------------------
    // ListFiles
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Lists files at a given path on the connected phone."
                         , Examples = new[]
                                      {
                                              "List files on my phone in Documents"
                                            , "What files are in the Notes folder on my phone?"
                                            , "Show files at /storage/Documents"
                                            , "List phone files"
                                      }
                         , Category = "file-sync")]
    public async Task<string> ListFiles( [NaturalLanguageParam(Description = "The remote path on the connected phone to list, e.g. '/storage/emulated/0/Documents'."
                                                             , AllowEmpty  = false)]
                                         string path )
    {
        if (_phoneProvider.IsConnected.Not()) return NotConnectedMessage();

        try
        {
            var files = await _phoneProvider.ListFilesAsync(path);

            if (files.Count == 0) return $"No files found at '{path}'.";

            var sb = new StringBuilder();
            sb.AppendLine($"Files at '{path}' on {_phoneProvider.DeviceName} ({files.Count} items):");

            foreach (var file in files)
            {
                var sizeKb = file.SizeBytes / 1024.0;
                sb.AppendLine($"  • {file.RelativePath}  ({sizeKb:F1} KB)  modified {file.LastModified.ToLocalTime():yyyy-MM-dd HH:mm}");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return FileSyncUnavailableMessage();
        }
    }

    // -----------------------------------------------------------------------
    // SyncFolder
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Synchronises a local folder to a path on the connected phone."
                         , Examples = new[]
                                      {
                                              "Sync my notes folder to my phone"
                                            , "Copy my Documents to /storage/Documents on the phone"
                                            , "Sync C:\\Notes to /storage/Notes on my phone"
                                            , "Transfer my notes to the phone"
                                            , "Backup my documents to my phone"
                                      }
                         , Category = "file-sync")]
    public async Task<string> SyncFolder( [NaturalLanguageParam(Description = "The local source folder path to sync, e.g. 'C:\\Users\\ben\\Documents\\Notes'."
                                                               , AllowEmpty  = false)]
                                          string localPath
                                        , [NaturalLanguageParam(Description = "The destination path on the phone, e.g. '/storage/emulated/0/Documents/Notes'."
                                                               , AllowEmpty  = false)]
                                          string remotePath )
    {
        if (_phoneProvider.IsConnected.Not()) return NotConnectedMessage();

        try
        {
            var result = await _syncService.SyncFolderAsync(localPath, remotePath);
            return FormatSyncResult(result, _phoneProvider.DeviceName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return FileSyncUnavailableMessage();
        }
    }

    // -----------------------------------------------------------------------
    // GetSyncStatus
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Shows the current sync delta between a local folder and a path on the connected phone — no files are copied."
                         , Examples = new[]
                                      {
                                              "Is my phone in sync?"
                                            , "What's the sync status of my Documents folder?"
                                            , "Are my files up to date on my phone?"
                                            , "Preview sync for my notes"
                                            , "Check sync status between C:\\Notes and /storage/Notes"
                                      }
                         , Category = "file-sync")]
    public async Task<string> GetSyncStatus( [NaturalLanguageParam(Description = "The local folder to check, e.g. 'C:\\Users\\ben\\Documents\\Notes'."
                                                                  , AllowEmpty  = false)]
                                             string localPath
                                           , [NaturalLanguageParam(Description = "The path on the phone to compare against, e.g. '/storage/emulated/0/Documents/Notes'."
                                                                  , AllowEmpty  = false)]
                                             string remotePath )
    {
        if (_phoneProvider.IsConnected.Not()) return NotConnectedMessage();

        try
        {
            var result = await _syncService.PreviewSyncAsync(localPath, remotePath);
            return FormatPreviewResult(result, _phoneProvider.DeviceName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return FileSyncUnavailableMessage();
        }
    }

    // -----------------------------------------------------------------------
    // ResolveSyncConflict
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Resolves a file sync conflict by choosing which version to keep."
                         , Examples = new[]
                                      {
                                              "Keep the phone version of README.md"
                                            , "Use my laptop version for the conflict"
                                            , "Resolve conflict — keep laptop"
                                            , "Keep the newest version of the conflict"
                                      }
                         , Category = "file-sync")]
    public async Task<string> ResolveSyncConflict( [NaturalLanguageParam(Description = "The relative file path reported in the conflict list, e.g. 'notes/README.md'."
                                                                        , AllowEmpty  = false)]
                                                   string conflictId
                                                 , [NaturalLanguageParam(Description = "Which version to keep: 'laptop', 'phone', or 'newest'."
                                                                        , AllowEmpty  = false)]
                                                   string resolution )
    {
        if (_phoneProvider.IsConnected.Not()) return NotConnectedMessage();

        try
        {
            return await _syncService.ResolveSyncConflictAsync(conflictId, resolution);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex;
            return FileSyncUnavailableMessage();
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string FormatSyncResult(SyncResult result, string deviceName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Sync to {deviceName}: {result.Summary}");

        foreach (var conflict in result.Conflicts)
        {
            sb.AppendLine($"  ⚠ Conflict: {conflict.RelativePath}");
            sb.AppendLine($"    Laptop: modified {conflict.SourceModified.ToLocalTime():yyyy-MM-dd HH:mm}");
            sb.AppendLine($"    Phone:  modified {conflict.DestinationModified.ToLocalTime():yyyy-MM-dd HH:mm}");
            sb.AppendLine($"    Say ‘resolve conflict {conflict.RelativePath} — keep laptop/phone/newest’ to resolve.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatPreviewResult(SyncResult result, string deviceName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Sync preview ({deviceName}): {result.Summary}");

        foreach (var conflict in result.Conflicts)
            sb.AppendLine($"  ⚠ Conflict: {conflict.RelativePath}");

        return sb.ToString().TrimEnd();
    }

    private static string NotConnectedMessage()
        => "Your phone is not connected for file sync. "
         + "Make sure the CP app is running on your phone and on the same local network, then try again.";

    private static string FileSyncUnavailableMessage()
        => "File sync is unavailable right now. "
         + "Make sure your phone is on the same network and the CP app is running, then try again.";
}
