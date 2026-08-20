using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;

namespace CognitivePlatform.Api.Domains.Secrets;

public sealed class SecretVaultService : ISecretVaultService
{
    private const string SentinelTitle = "SentinelSecretRecord";
    private const string SentinelText  = "sentinel";

    private static readonly byte[] StaticSalt = Encoding.UTF8.GetBytes("CognitivePlatformSecretsVaultStaticSalt123!");

    private readonly IObjectStore _store;

    private byte[]?          _cachedKey;
    private DateTimeOffset?  _lastAccessedUtc;

    public SecretVaultService(IObjectStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public bool IsInitialized()
    {
        var sentinel = _store.List<SecretEntry>()
                             .FirstOrDefault(secret => secret.Title == SentinelTitle);
        return sentinel is not null;
    }

    public bool IsUnlocked()
    {
        if (_cachedKey is null)
        {
            return false;
        }

        if (_lastAccessedUtc.HasValue 
         && DateTimeOffset.UtcNow - _lastAccessedUtc.Value > TimeSpan.FromMinutes(5))
        {
            Lock();
            return false;
        }

        _lastAccessedUtc = DateTimeOffset.UtcNow;
        return true;
    }

    public async Task<bool> SetupAsync(string pin)
    {
        if (pin.HasNoValue())
        {
            return false;
        }

        var key = DeriveKey(pin);
        var (payload, nonce, tag) = EncryptInternal(SentinelText, key);

        var sentinel = new SecretEntry
                       {
                           Id               = Guid.NewGuid().ToString("N")
                         , Title            = SentinelTitle
                         , Category         = "System"
                         , EncryptedPayload = payload
                         , Nonce            = nonce
                         , AuthTag          = tag
                         , CreatedUtc       = DateTimeOffset.UtcNow
                       };

        await _store.Save(sentinel, id: sentinel.Id).ConfigureAwait(false);

        _cachedKey       = key;
        _lastAccessedUtc = DateTimeOffset.UtcNow;

        return true;
    }

    public async Task<bool> UnlockAsync(string pin)
    {
        if (pin.HasNoValue())
        {
            return false;
        }

        var sentinel = _store.List<SecretEntry>()
                             .FirstOrDefault(secret => secret.Title == SentinelTitle);
        if (sentinel is null)
        {
            return false;
        }

        var key = DeriveKey(pin);

        try
        {
            var decrypted = DecryptInternal(sentinel.EncryptedPayload, sentinel.Nonce, sentinel.AuthTag, key);
            if (decrypted == SentinelText)
            {
                _cachedKey       = key;
                _lastAccessedUtc = DateTimeOffset.UtcNow;
                return true;
            }
        }
        catch
        {
            // Decryption failure indicates incorrect PIN
        }

        return false;
    }

    public void Lock()
    {
        if (_cachedKey is not null)
        {
            Array.Clear(_cachedKey, 0, _cachedKey.Length);
            _cachedKey = null;
        }
        _lastAccessedUtc = null;
    }

    public Task<(string EncryptedPayload, string Nonce, string AuthTag)> EncryptAsync(string plaintext)
    {
        if (!IsUnlocked() || _cachedKey is null)
        {
            throw new InvalidOperationException("Secrets vault is locked.");
        }

        var result = EncryptInternal(plaintext, _cachedKey);
        _lastAccessedUtc = DateTimeOffset.UtcNow;

        return Task.FromResult(result);
    }

    public Task<string> DecryptAsync(string encryptedPayload, string nonce, string authTag)
    {
        if (!IsUnlocked() || _cachedKey is null)
        {
            throw new InvalidOperationException("Secrets vault is locked.");
        }

        var result = DecryptInternal(encryptedPayload, nonce, authTag, _cachedKey);
        _lastAccessedUtc = DateTimeOffset.UtcNow;

        return Task.FromResult(result);
    }

    // ── Helper Cryptography Methods ──────────────────────────────────────────

    private static byte[] DeriveKey(string pin)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            pin
          , StaticSalt
          , iterations: 100000
          , HashAlgorithmName.SHA256
          , outputLength: 32);
    }

    private static (string EncryptedPayload, string Nonce, string AuthTag) EncryptInternal(string plaintext, byte[] key)
    {
        using var aesGcm = new AesGcm(key, tagSizeInBytes: 16);
        
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        
        var tag = new byte[16];
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];

        aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return (
            Convert.ToBase64String(ciphertext)
          , Convert.ToBase64String(nonce)
          , Convert.ToBase64String(tag)
        );
    }

    private static string DecryptInternal(string encryptedPayload, string nonce, string authTag, byte[] key)
    {
        using var aesGcm = new AesGcm(key, tagSizeInBytes: 16);

        var ciphertext = Convert.FromBase64String(encryptedPayload);
        var nonceBytes = Convert.FromBase64String(nonce);
        var tagBytes = Convert.FromBase64String(authTag);
        var plaintextBytes = new byte[ciphertext.Length];

        aesGcm.Decrypt(nonceBytes, ciphertext, tagBytes, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
