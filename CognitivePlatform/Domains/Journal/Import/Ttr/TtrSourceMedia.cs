namespace CognitivePlatform.Api.Domains.Journal.Import.Ttr;

internal sealed record TtrSourceMedia(long Id, byte[]? Bytes, string FileName, int Type, long EntryId);
