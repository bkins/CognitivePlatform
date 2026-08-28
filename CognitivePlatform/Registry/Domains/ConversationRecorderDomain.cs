using System.Collections.Generic;

namespace CognitivePlatform.Api.Registry.Domains;

public sealed record ConversationRecorderDomain : IDomainDefinition
{
    public string Name        => "Conversations";
    public string Description => "Conversation audio recording, speaker diarization, transcripts, and cognitive memory recollection.";

    public IReadOnlyList<string> Keywords => new[]
    {
        "conversation"
      , "conversations"
      , "recording"
      , "recordings"
      , "transcript"
      , "transcripts"
      , "speaker"
      , "speakers"
      , "diarization"
      , "audio"
      , "recollection"
      , "meeting"
      , "discuss"
      , "discussed"
    };
}
