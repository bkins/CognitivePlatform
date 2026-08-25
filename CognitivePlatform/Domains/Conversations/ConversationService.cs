using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CP.Shared.Primitives.Avails.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CognitivePlatform.Api.Domains.Conversations;

public class ConversationService : IConversationService
{
    private readonly IObjectStore                 _objectStore;
    private readonly ITranscriptionService        _transcriptionService;
    private readonly ISpeakerDiarizationService   _diarizationService;
    private readonly ILogger<ConversationService> _logger;
    private readonly string                       _recordingsDirectory;

    public ConversationService( IObjectStore                 objectStore
                              , ITranscriptionService        transcriptionService
                              , ISpeakerDiarizationService   diarizationService
                              , ILogger<ConversationService> logger
                              , IHostEnvironment?            hostEnv = null )
    {
        _objectStore          = objectStore;
        _transcriptionService = transcriptionService;
        _diarizationService   = diarizationService;
        _logger               = logger;

        var envName = hostEnv?.EnvironmentName ?? "Development";
        if (OperatingSystem.IsWindows() && Directory.Exists(@"C:\CP\Data"))
        {
            _recordingsDirectory = Path.Combine(@"C:\CP\Data", envName, "Recordings");
        }
        else
        {
            _recordingsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", envName, "Recordings");
        }

        if (!Directory.Exists(_recordingsDirectory))
        {
            Directory.CreateDirectory(_recordingsDirectory);
        }
    }

    public async Task<ConversationRecord> CreateRecordingAsync( ConversationRecord record, CancellationToken cancellationToken = default )
    {
        if (record.Id == Guid.Empty)
        {
            record.Id = Guid.NewGuid();
        }

        await _objectStore.Save(record, partitionKey: null, id: record.Id.ToString());
        return record;
    }

    public async Task<ConversationRecord?> GetRecordingAsync( Guid id, CancellationToken cancellationToken = default )
    {
        var record = await _objectStore.GetAsync<ConversationRecord>(id.ToString(), partitionKey: null, cancellationToken: cancellationToken);
        if (record != null && record.IsDeleted)
        {
            return null;
        }
        return record;
    }

    public async Task<List<ConversationRecord>> ListRecordingsAsync( CancellationToken cancellationToken = default )
    {
        var items = await _objectStore.ListAsync<ConversationRecord>(partitionKey: null, fromUtc: null, toUtc: null, cancellationToken: cancellationToken);
        return items.Where(record => !record.IsDeleted)
                    .OrderByDescending(record => record.RecordedAtUtc)
                    .ToList();
    }

    public async Task<bool> DeleteRecordingAsync( Guid id, CancellationToken cancellationToken = default )
    {
        var record = await GetRecordingAsync(id, cancellationToken);
        if (record == null)
        {
            return false;
        }

        record.IsDeleted = true;
        record.DeletedUtc = DateTime.UtcNow;
        await _objectStore.Save(record, partitionKey: null, id: record.Id.ToString());

        var transcript = await GetTranscriptAsync(id, cancellationToken);
        if (transcript != null)
        {
            transcript.IsDeleted = true;
            transcript.DeletedUtc = DateTime.UtcNow;
            await _objectStore.Save(transcript, partitionKey: null, id: $"transcript_{id}");
        }

        var filePath = Path.Combine(_recordingsDirectory, $"recording_{id}.wav");
        if (File.Exists(filePath))
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete audio file {FilePath}", filePath);
            }
        }

        return true;
    }

    public async Task<bool> SaveAudioAsync( Guid conversationId
                                          , Stream audioStream
                                          , string mimeType = "audio/wav"
                                          , CancellationToken cancellationToken = default )
    {
        if (audioStream == null || (audioStream.CanSeek && audioStream.Length == 0))
        {
            return false;
        }

        using var memoryStream = new MemoryStream();
        await audioStream.CopyToAsync(memoryStream, cancellationToken);
        var audioBytes = memoryStream.ToArray();
        if (audioBytes.Length == 0)
        {
            return false;
        }

        var record = await GetRecordingAsync(conversationId, cancellationToken);
        if (record == null)
        {
            record = new ConversationRecord
            {
                Id            = conversationId
              , RecordedAtUtc = DateTime.UtcNow
              , Status        = TranscriptionStatus.NotProcessed
            };
        }

        var filePath = Path.Combine(_recordingsDirectory, $"recording_{conversationId}.wav");
        var tempPath = Path.Combine(_recordingsDirectory, $"temp_{Guid.NewGuid()}.tmp");

        try
        {
            await File.WriteAllBytesAsync(tempPath, audioBytes, cancellationToken);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveAudioAsync IOException] {ex.Message}");
            if (!File.Exists(filePath))
            {
                throw;
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }

        var fileLength = File.Exists(filePath) ? new FileInfo(filePath).Length : audioBytes.Length;

        record.AudioFilePath = filePath;
        record.FileSizeBytes = fileLength;
        record.MimeType      = mimeType.HasValue() ? mimeType : "audio/wav";

        await _objectStore.Save(record, partitionKey: null, id: record.Id.ToString());
        return true;
    }

    public async Task<(Stream? Stream, string ContentType)> GetAudioAsync( Guid conversationId
                                                                         , CancellationToken cancellationToken = default )
    {
        var record = await GetRecordingAsync(conversationId, cancellationToken);
        var filePath = record?.AudioFilePath.HasValue() == true
            ? record!.AudioFilePath
            : Path.Combine(_recordingsDirectory, $"recording_{conversationId}.wav");

        if (File.Exists(filePath))
        {
            var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return (fileStream, record?.MimeType.HasValue() == true ? record.MimeType : "audio/wav");
        }

        return (null, "audio/wav");
    }

    public async Task<Transcript> ProcessTranscriptionAsync( Guid conversationId
                                                          , Stream audioStream
                                                          , string mimeType = "audio/wav"
                                                          , CancellationToken cancellationToken = default )
    {
        using var memoryStream = new MemoryStream();
        if (audioStream != null)
        {
            await audioStream.CopyToAsync(memoryStream, cancellationToken);
        }
        var audioBytes = memoryStream.ToArray();

        var filePath = Path.Combine(_recordingsDirectory, $"recording_{conversationId}.wav");

        if (audioBytes.Length > 0)
        {
            if (!File.Exists(filePath) || new FileInfo(filePath).Length != audioBytes.Length)
            {
                using var saveStream = new MemoryStream(audioBytes);
                await SaveAudioAsync(conversationId, saveStream, mimeType, cancellationToken);
            }
        }
        else if (File.Exists(filePath))
        {
            audioBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }

        var record = await GetRecordingAsync(conversationId, cancellationToken);
        if (record == null)
        {
            record = new ConversationRecord
            {
                Id            = conversationId
              , RecordedAtUtc = DateTime.UtcNow
              , Status        = TranscriptionStatus.Processing
            };
        }
        else
        {
            record.Status = TranscriptionStatus.Processing;
        }
        await _objectStore.Save(record, partitionKey: null, id: record.Id.ToString());

        using var transcribeStream = new MemoryStream(audioBytes);
        var transcript = await _transcriptionService.TranscribeAudioAsync(conversationId, transcribeStream, mimeType, cancellationToken);

        await _objectStore.Save(transcript, partitionKey: null, id: $"transcript_{conversationId}");

        record.Status = transcript.Status;
        await _objectStore.Save(record, partitionKey: null, id: record.Id.ToString());

        return transcript;
    }

    public async Task<Transcript> DiarizeTranscriptAsync( Guid conversationId
                                                        , Stream audioStream
                                                        , CancellationToken cancellationToken = default )
    {
        var transcript = await GetTranscriptAsync(conversationId, cancellationToken);
        if (transcript == null)
        {
            return new Transcript
            {
                ConversationId = conversationId
              , Status         = TranscriptionStatus.Failed
              , ErrorMessage   = "Transcript not found for diarization."
            };
        }

        var diarized = await _diarizationService.DiarizeTranscriptAsync(transcript, audioStream, cancellationToken);
        await _objectStore.Save(diarized, partitionKey: null, id: $"transcript_{conversationId}");

        return diarized;
    }

    public async Task<Transcript?> GetTranscriptAsync( Guid conversationId, CancellationToken cancellationToken = default )
    {
        var transcript = await _objectStore.GetAsync<Transcript>($"transcript_{conversationId}", partitionKey: null, cancellationToken: cancellationToken);
        if (transcript != null && transcript.IsDeleted)
        {
            return null;
        }
        return transcript;
    }

    public async Task<Transcript?> MapParticipantsAsync( Guid conversationId
                                                       , Dictionary<string, string> speakerMap
                                                       , CancellationToken cancellationToken = default )
    {
        var transcript = await GetTranscriptAsync(conversationId, cancellationToken);
        if (transcript == null)
        {
            return null;
        }

        foreach (var segment in transcript.Segments)
        {
            if (segment.SpeakerLabel.HasValue() && speakerMap.TryGetValue(segment.SpeakerLabel, out var mappedName))
            {
                segment.SpeakerName = mappedName;
            }
        }

        await _objectStore.Save(transcript, partitionKey: null, id: $"transcript_{conversationId}");

        foreach (var (speakerLabel, displayName) in speakerMap)
        {
            var participant = new ConversationParticipant
            {
                Id             = Guid.NewGuid()
              , ConversationId = conversationId
              , SpeakerLabel   = speakerLabel
              , DisplayName    = displayName
            };
            await _objectStore.Save(participant, partitionKey: null, id: $"participant_{conversationId}_{speakerLabel}");
        }

        return transcript;
    }

    public async Task<List<ConversationParticipant>> GetParticipantsAsync( Guid conversationId, CancellationToken cancellationToken = default )
    {
        var items = await _objectStore.ListAsync<ConversationParticipant>(partitionKey: null, fromUtc: null, toUtc: null, cancellationToken: cancellationToken);
        return items.Where(item => item.ConversationId == conversationId && !item.IsDeleted)
                    .ToList();
    }

    public async Task<ConversationDetails?> GetConversationDetailsAsync( Guid conversationId, CancellationToken cancellationToken = default )
    {
        var record = await GetRecordingAsync(conversationId, cancellationToken);
        if (record == null)
        {
            var existingTranscript = await GetTranscriptAsync(conversationId, cancellationToken);
            if (existingTranscript == null)
            {
                return null;
            }

            record = new ConversationRecord
            {
                Id            = conversationId
              , Title         = "Untitled Conversation"
              , Status        = existingTranscript.Status
              , RecordedAtUtc = existingTranscript.ProcessedAtUtc ?? DateTime.UtcNow
            };
            await _objectStore.Save(record, partitionKey: null, id: conversationId.ToString());
        }

        var transcript   = await GetTranscriptAsync(conversationId, cancellationToken);
        var participants = await GetParticipantsAsync(conversationId, cancellationToken);

        return new ConversationDetails
        {
            Record       = record
          , Transcript   = transcript
          , Participants = participants
        };
    }

    public async Task<List<ConversationRecord>> SearchConversationsAsync( string? query = null
                                                                        , string? participantName = null
                                                                        , DateTimeOffset? fromDate = null
                                                                        , DateTimeOffset? toDate = null
                                                                        , CancellationToken cancellationToken = default )
    {
        var records = await ListRecordingsAsync(cancellationToken);

        if (fromDate.HasValue)
        {
            records = records.Where(r => r.RecordedAtUtc >= fromDate.Value.UtcDateTime).ToList();
        }

        if (toDate.HasValue)
        {
            records = records.Where(r => r.RecordedAtUtc <= toDate.Value.UtcDateTime).ToList();
        }

        if (query.HasNoValue() && participantName.HasNoValue())
        {
            return records;
        }

        var matchingIds = new HashSet<Guid>();

        foreach (var record in records)
        {
            if (query.HasValue() && record.Title.Contains(query!, StringComparison.OrdinalIgnoreCase))
            {
                matchingIds.Add(record.Id);
                continue;
            }

            var details = await GetConversationDetailsAsync(record.Id, cancellationToken);
            if (details == null)
            {
                continue;
            }

            if (participantName.HasValue() && details.Participants.Any(p => p.DisplayName != null && p.DisplayName.Contains(participantName!, StringComparison.OrdinalIgnoreCase)))
            {
                matchingIds.Add(record.Id);
                continue;
            }

            if (query.HasValue() && details.Transcript != null)
            {
                if (details.Transcript.Segments.Any(s => s.Text.Contains(query!, StringComparison.OrdinalIgnoreCase) || (s.SpeakerName != null && s.SpeakerName.Contains(query!, StringComparison.OrdinalIgnoreCase))))
                {
                    matchingIds.Add(record.Id);
                }
            }
        }

        return records.Where(r => matchingIds.Contains(r.Id)).ToList();
    }
}
