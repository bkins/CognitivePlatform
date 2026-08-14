using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Domains.Secrets;
using Moq;
using Xunit;

namespace CognitivePlatform.Tests;

public sealed class SecretsTests
{
    private readonly Mock<IObjectStore> _storeMock = new();
    private readonly SecretVaultService _vaultService;
    private readonly SecretActions      _actions;

    private readonly List<SecretEntry> _storedSecrets = new();

    public SecretsTests()
    {
        _vaultService = new SecretVaultService(_storeMock.Object);
        _actions      = new SecretActions(_vaultService, _storeMock.Object);

        // Setup store Mock to simulate persistence
        _storeMock.Setup(store => store.Save(It.IsAny<SecretEntry>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<SecretEntry, string, string?>((entry, id, partitionKey) =>
                  {
                      _storedSecrets.RemoveAll(secret => secret.Id == id);
                      _storedSecrets.Add(entry);
                  })
                  .ReturnsAsync((SecretEntry entry, string id, string? partitionKey) => id);

        _storeMock.Setup(store => store.List<SecretEntry>(It.IsAny<string>(), null, null))
                  .Returns(() => _storedSecrets.Where(secret => !secret.IsDeleted).ToList());

        _storeMock.Setup(store => store.SoftDelete<SecretEntry>(It.IsAny<string>(), It.IsAny<string>()))
                  .Callback<string, string?>((id, partitionKey) =>
                  {
                      var entry = _storedSecrets.FirstOrDefault(secret => secret.Id == id);
                      if (entry is not null)
                      {
                          entry.IsDeleted  = true;
                          entry.DeletedUtc = DateTimeOffset.UtcNow;
                      }
                  })
                  .Returns(true);
    }

    [Fact]
    public async Task Setup_InitializesVault_WithCorrectSentinel()
    {
        var pin = "1234";

        var result = await _vaultService.SetupAsync(pin);

        Assert.True(result);
        Assert.True(_vaultService.IsInitialized());
        Assert.True(_vaultService.IsUnlocked());
        Assert.Single(_storedSecrets);
        Assert.Equal("SentinelSecretRecord", _storedSecrets[0].Title);
        Assert.Equal("System",               _storedSecrets[0].Category);
    }

    [Fact]
    public async Task Unlock_ReturnsTrue_WithCorrectPin()
    {
        var pin = "1234";
        await _vaultService.SetupAsync(pin);
        _vaultService.Lock();

        var result = await _vaultService.UnlockAsync(pin);

        Assert.True(result);
        Assert.True(_vaultService.IsUnlocked());
    }

    [Fact]
    public async Task Unlock_ReturnsFalse_WithIncorrectPin()
    {
        var pin = "1234";
        await _vaultService.SetupAsync(pin);
        _vaultService.Lock();

        var result = await _vaultService.UnlockAsync("9999");

        Assert.False(result);
        Assert.False(_vaultService.IsUnlocked());
    }

    [Fact]
    public async Task EncryptDecrypt_RoundtripsSuccessfully()
    {
        var pin = "1234";
        var plaintext = "MySuperSecretValue!123";
        await _vaultService.SetupAsync(pin);

        var (payload, nonce, tag) = await _vaultService.EncryptAsync(plaintext);
        var decrypted = await _vaultService.DecryptAsync(payload, nonce, tag);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public async Task Lock_WipesCachedKey()
    {
        var pin = "1234";
        await _vaultService.SetupAsync(pin);

        _vaultService.Lock();

        Assert.False(_vaultService.IsUnlocked());
        await Assert.ThrowsAsync<InvalidOperationException>(() => _vaultService.EncryptAsync("test"));
    }

    [Fact]
    public async Task SaveSecret_PersistsEncryptedEntry()
    {
        var pin = "1234";
        await _vaultService.SetupAsync(pin);

        var result = await _actions.SaveSecret("API Token", "abc-123-xyz", "Development");

        Assert.Contains("saved successfully", result);
        // Exclude sentinel record to check user secret
        var userSecret = _storedSecrets.FirstOrDefault(secret => secret.Title == "API Token");
        Assert.NotNull(userSecret);
        Assert.Equal("Development", userSecret.Category);
        Assert.NotEmpty(userSecret.EncryptedPayload);
        Assert.NotEqual("abc-123-xyz", userSecret.EncryptedPayload);
    }

    [Fact]
    public async Task GetSecret_DecryptsAndReturnsPayload()
    {
        var pin = "1234";
        var originalValue = "SSN-999-88-7777";
        await _vaultService.SetupAsync(pin);
        await _actions.SaveSecret("SSN", originalValue, "Personal");

        var result = await _actions.GetSecret("SSN");

        Assert.Contains("SSN",           result);
        Assert.Contains("Personal",      result);
        Assert.Contains(originalValue,   result);
    }

    [Fact]
    public async Task ListSecrets_ReturnsTitlesWithoutPayloads()
    {
        var pin = "1234";
        await _vaultService.SetupAsync(pin);
        await _actions.SaveSecret("Secret1", "val1", "CatA");
        await _actions.SaveSecret("Secret2", "val2", "CatB");

        var result = await _actions.ListSecrets();

        Assert.Contains("Secret1", result);
        Assert.Contains("CatA",    result);
        Assert.Contains("Secret2", result);
        Assert.Contains("CatB",    result);
        Assert.DoesNotContain("val1", result);
        Assert.DoesNotContain("val2", result);
    }

    [Fact]
    public async Task DeleteSecret_MarksEntryAsDeleted()
    {
        var pin = "1234";
        await _vaultService.SetupAsync(pin);
        await _actions.SaveSecret("SSN", "ssn_val", "Personal");

        var result = await _actions.DeleteSecret("SSN");

        Assert.Contains("deleted successfully", result);
        var ssnSecret = _storedSecrets.FirstOrDefault(secret => secret.Title == "SSN");
        Assert.NotNull(ssnSecret);
        Assert.True(ssnSecret.IsDeleted);
    }
}
