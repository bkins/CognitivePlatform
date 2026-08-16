using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CognitivePlatform.Api.Attributes;
using CognitivePlatform.Api.Data;
using CognitivePlatform.Api.Execution;
using CognitivePlatform.Api.Registry.Domains;
using CP.Shared.Primitives.Avails.Extensions;

namespace CognitivePlatform.Api.Domains.Secrets;

[Category("secrets")]
[Domain(typeof(SecretsDomain))]
public sealed class SecretActions
{
    private readonly ISecretVaultService _vault;
    private readonly IObjectStore        _store;

    public SecretActions(ISecretVaultService vault, IObjectStore store)
    {
        _vault = vault ?? throw new ArgumentNullException(nameof(vault));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    [FastPath]
    [NaturalLanguageAction(
        Description = "Saves a sensitive secret entry into the encrypted vault."
      , Examples = new[]
                   {
                       "Save secret 'Gmail password' is 'my-password-123'"
                     , "Save sensitive credentials for 'Bank API' with value 'secret_token_abc'"
                     , "Store secret 'social security number' as '000-11-2222' under category 'Personal'"
                   }
      , Category = "secrets"
      , IsReplayable = false)]
    public async Task<string> SaveSecret(
        [NaturalLanguageParam(Description = "The title or label of the secret.")]
        string title
      , [NaturalLanguageParam(Description = "The sensitive payload or secret value to encrypt.")]
        string secretValue
      , [NaturalLanguageParam(Description = "Optional category for categorization (e.g. Credentials, Personal, Health).", Optional = true, DefaultValue = "General")]
        string? category = null)
    {
        if (title.HasNoValue() || secretValue.HasNoValue())
        {
            return "Failed to save secret: title and secret value must be provided.";
        }

        var effectiveCategory = category.HasValue() ? category! : "General";

        // Encrypt the payload
        var (payload, nonce, tag) = await _vault.EncryptAsync(secretValue).ConfigureAwait(false);

        var secretEntry = new SecretEntry
                          {
                              Id               = Guid.NewGuid().ToString("N")
                            , Title            = title
                            , Category         = effectiveCategory
                            , EncryptedPayload = payload
                            , Nonce            = nonce
                            , AuthTag          = tag
                            , CreatedUtc       = DateTimeOffset.UtcNow
                          };

        await _store.Save(secretEntry, id: secretEntry.Id).ConfigureAwait(false);

        return $"Secret '{title}' saved successfully under category '{effectiveCategory}'.";
    }

    [FastPath]
    [NaturalLanguageAction(
        Description = "Retrieves and decrypts a secret entry by title or ID after authentication."
      , Examples = new[]
                   {
                       "Get secret 'Gmail password'"
                     , "Retrieve secret for 'Bank API'"
                     , "Show secret 'social security number'"
                   }
      , Category = "secrets"
      , IsReplayable = false)]
    public async Task<string> GetSecret(
        [NaturalLanguageParam(Description = "The title or ID of the secret to retrieve.")]
        string titleOrId)
    {
        if (titleOrId.HasNoValue())
        {
            return "Failed to retrieve secret: title or ID must be provided.";
        }

        var secret = _store.List<SecretEntry>()
                           .FirstOrDefault(s => s.Title.EqualsIgnoreCase(titleOrId)
                                             || s.Id.EqualsIgnoreCase(titleOrId));

        if (secret is null || secret.Title == "SentinelSecretRecord")
        {
            return $"Secret '{titleOrId}' not found.";
        }

        var decryptedValue = await _vault.DecryptAsync(secret.EncryptedPayload, secret.Nonce, secret.AuthTag)
                                         .ConfigureAwait(false);

        return $"Secret '{secret.Title}' (Category: {secret.Category}): {decryptedValue}";
    }

    [FastPath]
    [NaturalLanguageAction(
        Description = "Lists secret titles and categories without revealing decrypted payloads."
      , Examples = new[]
                   {
                       "List my secrets"
                     , "Show all secrets in the vault"
                     , "List secrets under category 'Personal'"
                   }
      , Category = "secrets"
      , IsReplayable = true)]
    public Task<string> ListSecrets(
        [NaturalLanguageParam(Description = "Optional category to filter by.", Optional = true)]
        string? category = null)
    {
        var secrets = _store.List<SecretEntry>()
                            .Where(s => s.Title != "SentinelSecretRecord");

        if (category.HasValue())
        {
            secrets = secrets.Where(s => s.Category.EqualsIgnoreCase(category));
        }

        var secretList = secrets.ToList();

        if (secretList.Count == 0)
        {
            return Task.FromResult(category.HasValue() 
                ? $"No secrets found under category '{category}'." 
                : "No secrets found in the vault.");
        }

        var builder = new StringBuilder();
        builder.AppendLine("Secrets in vault:");
        foreach (var secret in secretList)
        {
            builder.AppendLine($"- Title: {secret.Title}, Category: {secret.Category}");
        }

        return Task.FromResult(builder.ToString().TrimEnd());
    }

    [FastPath]
    [DestructiveAction]
    [NaturalLanguageAction(
        Description = "Soft-deletes a secret entry from the vault."
      , Examples = new[]
                   {
                       "Delete secret 'Gmail password'"
                     , "Remove secret 'social security number'"
                   }
      , Category = "secrets"
      , IsReplayable = false)]
    public Task<string> DeleteSecret(
        [NaturalLanguageParam(Description = "The title or ID of the secret to delete.")]
        string titleOrId)
    {
        if (titleOrId.HasNoValue())
        {
            return Task.FromResult("Failed to delete secret: title or ID must be provided.");
        }

        var secret = _store.List<SecretEntry>()
                           .FirstOrDefault(s => s.Title.EqualsIgnoreCase(titleOrId)
                                             || s.Id.EqualsIgnoreCase(titleOrId));

        if (secret is null || secret.Title == "SentinelSecretRecord")
        {
            return Task.FromResult($"Secret '{titleOrId}' not found.");
        }

        var deleted = _store.SoftDelete<SecretEntry>(secret.Id);

        return Task.FromResult(deleted 
            ? $"Secret '{secret.Title}' deleted successfully." 
            : $"Failed to delete secret '{secret.Title}'.");
    }
}
