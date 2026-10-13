namespace Realm.Shared.Metadata;

public delegate byte[] SetMetadataDelegate(ReadOnlySpan<byte> bytes, string? metadataJson);