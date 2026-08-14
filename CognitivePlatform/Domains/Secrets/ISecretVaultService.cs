using System.Threading.Tasks;

namespace CognitivePlatform.Api.Domains.Secrets;

public interface ISecretVaultService
{
    bool IsInitialized();
    bool IsUnlocked();
    Task<bool> SetupAsync(string pin);
    Task<bool> UnlockAsync(string pin);
    void Lock();
    Task<(string EncryptedPayload, string Nonce, string AuthTag)> EncryptAsync(string plaintext);
    Task<string> DecryptAsync(string encryptedPayload, string nonce, string authTag);
}
