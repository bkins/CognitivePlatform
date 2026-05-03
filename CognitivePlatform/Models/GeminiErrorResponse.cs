using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.Models;

public class GeminiErrorResponse
{
    [JsonPropertyName("error")]
    public ErrorContent Error { get; set; }
}

public class ErrorContent
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("details")]
    public List<ErrorDetail> Details { get; set; }
}

public class ErrorDetail
{
    [JsonPropertyName("@type")]
    public string Type { get; set; }

    // Fields for google.rpc.Help
    [JsonPropertyName("links")]
    public List<HelpLink> Links { get; set; }

    // Fields for google.rpc.QuotaFailure
    [JsonPropertyName("violations")]
    public List<QuotaViolation> Violations { get; set; }

    // Fields for google.rpc.RetryInfo
    [JsonPropertyName("retryDelay")]
    public string RetryDelay { get; set; }
}

public class HelpLink
{
    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }
}

public class QuotaViolation
{
    [JsonPropertyName("quotaMetric")]
    public string QuotaMetric { get; set; }

    [JsonPropertyName("quotaId")]
    public string QuotaId { get; set; }

    [JsonPropertyName("quotaDimensions")]
    public Dictionary<string, string> QuotaDimensions { get; set; }

    [JsonPropertyName("quotaValue")]
    public string QuotaValue { get; set; }
}