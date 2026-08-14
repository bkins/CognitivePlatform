using System;

namespace CognitivePlatform.Api.Domains.Secrets;

public sealed class SecretEntry
{
    public string          Id               { get; init; } = default!;
    public string          Title            { get; init; } = string.Empty;
    public string          Category         { get; init; } = "General";
    
    /// <summary>
    /// AES-256-GCM encrypted payload (base64 encoded).
    /// </summary>
    public string          EncryptedPayload { get; init; } = string.Empty;
    
    /// <summary>
    /// Base64 encoded Initialization Vector (IV/Nonce).
    /// </summary>
    public string          Nonce            { get; init; } = string.Empty;
    
    /// <summary>
    /// Base64 encoded authentication tag for GCM verification.
    /// </summary>
    public string          AuthTag          { get; init; } = string.Empty;
    
    public DateTimeOffset  CreatedUtc       { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ModifiedUtc      { get; set; }
    public bool            IsDeleted        { get; set; }
    public DateTimeOffset? DeletedUtc       { get; set; }
}
