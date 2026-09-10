using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.Domains.MemoryReview;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemoryReviewSourceKind
{
    Identity
  , Persona
  , Conversation
  , Knowledge
}
