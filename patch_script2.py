import sys

def patch_file(filepath, target, replacement):
    with open(filepath, "r") as f:
        content = f.read()

    if target in content:
        content = content.replace(target, replacement)
        with open(filepath, "w") as f:
            f.write(content)
        print(f"Patched {filepath}")
    else:
        print(f"Failed to patch {filepath}")

raud_target = """	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes) => ExtractMetadataFromBytes(bytes, MagicBytes);
	public static string? ExtractMetadata(Stream stream) => ExtractMetadataFromStream(stream, MagicBytes);
	public static string? ExtractMetadataFromFile(string filePath) => ExtractMetadataFromPath(filePath, MagicBytes);

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return SetContainerMetadata(bytes, MagicBytes, Name, Format, newMetadataJson);
	}"""
raud_replacement = """	public new static string? ExtractMetadata(ReadOnlySpan<byte> bytes) => ExtractMetadataFromBytes(bytes, MagicBytes);
	public new static string? ExtractMetadata(Stream stream) => ExtractMetadataFromStream(stream, MagicBytes);
	public new static string? ExtractMetadataFromFile(string filePath) => ExtractMetadataFromPath(filePath, MagicBytes);

	public new static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return SetContainerMetadata(bytes, MagicBytes, Name, Format, newMetadataJson);
	}"""
patch_file("Realm.Shared/Audio/RaudFile.cs", raud_target, raud_replacement)

rtex_target = """	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes) => ExtractMetadataFromBytes(bytes, MagicBytes);
	public static string? ExtractMetadata(Stream stream) => ExtractMetadataFromStream(stream, MagicBytes);
	public static string? ExtractMetadataFromFile(string filePath) => ExtractMetadataFromPath(filePath, MagicBytes);

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return SetContainerMetadata(bytes, MagicBytes, Name, Format, newMetadataJson);
	}"""
rtex_replacement = """	public new static string? ExtractMetadata(ReadOnlySpan<byte> bytes) => ExtractMetadataFromBytes(bytes, MagicBytes);
	public new static string? ExtractMetadata(Stream stream) => ExtractMetadataFromStream(stream, MagicBytes);
	public new static string? ExtractMetadataFromFile(string filePath) => ExtractMetadataFromPath(filePath, MagicBytes);

	public new static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return SetContainerMetadata(bytes, MagicBytes, Name, Format, newMetadataJson);
	}"""
patch_file("Realm.Shared/Textures/RtexFile.cs", rtex_target, rtex_replacement)

rkey_target = """	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes) => ExtractMetadataFromBytes(bytes, MagicBytes);
	public static string? ExtractMetadata(Stream stream) => ExtractMetadataFromStream(stream, MagicBytes);
	public static string? ExtractMetadataFromFile(string filePath) => ExtractMetadataFromPath(filePath, MagicBytes);

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return SetContainerMetadata(bytes, MagicBytes, Name, Format, newMetadataJson);
	}"""
rkey_replacement = """	public new static string? ExtractMetadata(ReadOnlySpan<byte> bytes) => ExtractMetadataFromBytes(bytes, MagicBytes);
	public new static string? ExtractMetadata(Stream stream) => ExtractMetadataFromStream(stream, MagicBytes);
	public new static string? ExtractMetadataFromFile(string filePath) => ExtractMetadataFromPath(filePath, MagicBytes);

	public new static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return SetContainerMetadata(bytes, MagicBytes, Name, Format, newMetadataJson);
	}"""
patch_file("Realm.Shared/Metadata/RkeyFile.cs", rkey_target, rkey_replacement)
