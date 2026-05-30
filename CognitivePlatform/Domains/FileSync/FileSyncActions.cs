using System.Text;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Integrations.FileSync;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.FileSync;

[Domain(typeof(FileSyncDomain))]
public class FileSyncActions
{
    private readonly IFileSyncProvider _phoneProvider;

    public FileSyncActions(IFileSyncProvider phoneProvider)
    {
        _phoneProvider = phoneProvider ?? throw new ArgumentNullException(nameof(phoneProvider));
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

    [NaturalLanguageAction(Description = "Synchronises a local folder to the connected phone."
                         , Examples = new[]
                                      {
                                              "Sync my notes folder to my phone"
                                            , "Copy my Documents to the phone"
                                            , "Sync Documents folder to my phone"
                                            , "Transfer my notes to the phone"
                                            , "Backup my documents to my phone"
                                      }
                         , Category = "file-sync")]
    public Task<string> SyncFolder( [NaturalLanguageParam(Description = "The local source folder path to sync, e.g. 'C:\\Users\\ben\\Documents\\Notes'."
                                                        , AllowEmpty  = false)]
                                    string sourcePath
                                  , [NaturalLanguageParam(Description = "The destination device, e.g. 'phone' or 'my phone'."
                                                        , AllowEmpty  = false)]
                                    string destinationDevice )
    {
        if (_phoneProvider.IsConnected.Not()) return Task.FromResult(NotConnectedMessage());

        return Task.FromResult(
            $"Sync from '{sourcePath}' to '{destinationDevice}' queued. "
          + "Full sync execution will be available in Phase F.1-D.");
    }

    // -----------------------------------------------------------------------
    // GetSyncStatus
    // -----------------------------------------------------------------------

    [NaturalLanguageAction(Description = "Reports the current sync status between this device and the connected phone."
                         , Examples = new[]
                                      {
                                              "Is my phone in sync?"
                                            , "What's the sync status?"
                                            , "Are my files up to date on my phone?"
                                            , "What files have changed?"
                                            , "Check sync status"
                                      }
                         , Category = "file-sync")]
    public Task<string> GetSyncStatus()
    {
        if (_phoneProvider.IsConnected.Not()) return Task.FromResult(NotConnectedMessage());

        return Task.FromResult(
            "Sync status: delta reporting will be available in Phase F.1-D. "
          + $"Phone device: {_phoneProvider.DeviceName}.");
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
    public Task<string> ResolveSyncConflict( [NaturalLanguageParam(Description = "The file path or conflict identifier reported in the conflict list."
                                                                  , AllowEmpty  = false)]
                                             string conflictId
                                           , [NaturalLanguageParam(Description = "Which version to keep: 'laptop', 'phone', or 'newest'."
                                                                  , AllowEmpty  = false)]
                                             string resolution )
    {
        if (_phoneProvider.IsConnected.Not()) return Task.FromResult(NotConnectedMessage());

        return Task.FromResult(
            $"Conflict for '{conflictId}' marked to keep: {resolution}. "
          + "Full conflict resolution will be applied in Phase F.1-D.");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string NotConnectedMessage()
        => "Your phone is not connected for file sync. "
         + "Make sure the CP app is running on your phone and on the same local network, then try again.";

    private static string FileSyncUnavailableMessage()
        => "File sync is unavailable right now. "
         + "Make sure your phone is on the same network and the CP app is running, then try again.";
}
