using System.Security.Cryptography;
using System.Text;

namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

public static class TtrDeterministicIdentity
{
    private static readonly Guid NamespaceId = Guid.Parse("22bcb236-865b-5f09-a29b-b49cb6600d7d");

    public static Guid CreateEntryId(string sourceInstance, long entryId)
    {
        return CreateUuid5(NamespaceId, $"{sourceInstance}|entry|{entryId}");
    }

    public static Guid CreateInitialRevisionId(string sourceInstance, long entryId)
    {
        return CreateUuid5(NamespaceId, $"{sourceInstance}|entry|{entryId}|revision|initial");
    }

    public static Guid CreateAttachmentId(string sourceInstance, long entryId, string mediaKey)
    {
        return CreateUuid5(NamespaceId, $"{sourceInstance}|entry|{entryId}|media|{mediaKey}");
    }

    public static Guid CreateBatchId(string sourceInstance, string planSha256)
    {
        return CreateUuid5(NamespaceId, $"{sourceInstance}|batch|{planSha256}");
    }

    public static Guid CreateItemId(string sourceInstance, long entryId)
    {
        return CreateUuid5(NamespaceId, $"{sourceInstance}|item|{entryId}");
    }

    private static Guid CreateUuid5(Guid namespaceId, string name)
    {
        var namespaceBytes = namespaceId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input     = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);

        var hash = SHA1.HashData(input);
        var uuid = hash[..16];
        uuid[6] = (byte)((uuid[6] & 0x0F) | 0x50);
        uuid[8] = (byte)((uuid[8] & 0x3F) | 0x80);
        SwapGuidByteOrder(uuid);
        return new Guid(uuid);
    }

    private static void SwapGuidByteOrder(byte[] bytes)
    {
        (bytes[0], bytes[3]) = (bytes[3], bytes[0]);
        (bytes[1], bytes[2]) = (bytes[2], bytes[1]);
        (bytes[4], bytes[5]) = (bytes[5], bytes[4]);
        (bytes[6], bytes[7]) = (bytes[7], bytes[6]);
    }
}
