using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.Domains.MemoryReview;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MemoryReviewState
{
    NeedsReview
  , Confirmed
  , Archived
}
