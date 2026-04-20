using System.Text.Json.Serialization;

namespace CognitivePlatform.Api.Contracts;

public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("data")]
    public T? Data { get; set; }

    public static ApiResponse<T> Ok (T data) => new()
                                                {
                                                    Success   = true
                                                  , Data      = data
                                                  , Timestamp = DateTime.UtcNow
                                                };

    public static ApiResponse<T> Fail (string error) => new()
                                                        {
                                                            Success   = false
                                                          , Error     = error
                                                          , Timestamp = DateTime.UtcNow
                                                        };
}
