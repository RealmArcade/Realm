using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
using Realm.EditorAPI;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;

namespace Realm.Shared.Metadata;

/// <summary>computing the canonical asset identifier.</summary>
public static partial class RealmMetadataHelper
	{
	public static string ComputeBlake3(byte[] bytes, string? extensionOrPath = null)
	{
		if (bytes == null || bytes.Length == 0)
		{
			return Hasher.Hash(ReadOnlySpan<byte>.Empty).ToString();
		}

		byte[] canonicalBytes = StripMetadataEphemeral(bytes, extensionOrPath);
		var hash = Hasher.Hash(canonicalBytes);
		return hash.ToString();
	}

	public static string ComputeBlake3(string filePath)
	{
		if (!File.Exists(filePath)) return string.Empty;
		byte[] bytes = File.ReadAllBytes(filePath);
		return ComputeBlake3(bytes, filePath);
	}

	public static string ComputeBlake3(Stream stream, string? extensionOrPath = null)
	{
		using var memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return ComputeBlake3(memoryStream.ToArray(), extensionOrPath);
	}

	public static string ComputeCanonicalAssetIdentifier(byte[] bytes, string extensionOrPath)
	{
		string extension = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		string blake3Hash = ComputeBlake3(bytes, extension);
		return string.IsNullOrEmpty(extension) ? blake3Hash : $"{blake3Hash}{extension}";
	}

	public static bool SyncBlake3Metadata(string filePath, string? precomputedCanonicalBlake3 = null)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		if (ext is not (".rtex" or ".ranim" or ".rmesh" or ".raud" or ".rkey")) return false;

		try
		{
			string? existingMeta = ExtractMetadata(filePath);
			JsonObject metaObj = ParseOrCreateMetadata(existingMeta, ext);

			if (IsBlake3UpToDate(metaObj, filePath, ref precomputedCanonicalBlake3))
			{
				return true;
			}

			string canonicalBlake3 = !string.IsNullOrEmpty(precomputedCanonicalBlake3) ? precomputedCanonicalBlake3 : ComputeBlake3(filePath);
			metaObj[AssetMetadataKeys.Blake3Hash] = canonicalBlake3;
			return AddMetadata(filePath, metaObj.ToJsonString());
		}
		catch
		{
			return false;
		}
	}

	private static bool IsBlake3UpToDate(JsonObject metaObj, string filePath, ref string? precomputedCanonicalBlake3)
	{
		if (metaObj.TryGetPropertyValue(AssetMetadataKeys.Blake3Hash, out var existingB3) && existingB3 != null)
		{
			string existingHashStr = existingB3.ToString();
			if (!string.IsNullOrEmpty(precomputedCanonicalBlake3) && string.Equals(existingHashStr, precomputedCanonicalBlake3, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (string.IsNullOrEmpty(precomputedCanonicalBlake3))
			{
				string canonical = ComputeBlake3(filePath);
				if (string.Equals(existingHashStr, canonical, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}

				precomputedCanonicalBlake3 = canonical;
			}
		}

		return false;
	}

	public static byte[] SyncBlake3MetadataBytes(byte[] bytes, string extensionOrPath)
	{
		if (bytes == null || bytes.Length == 0) return bytes ?? Array.Empty<byte>();

		string ext = NormalizeExtension(extensionOrPath);
		if (!IsSupportedBlake3Extension(ext)) return bytes;

		return TrySyncBlake3ForBytes(bytes, ext);
	}

	private static string NormalizeExtension(string extensionOrPath)
	{
		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
		return ext;
	}

	private static bool IsSupportedBlake3Extension(string ext)
	{
		return ext is ".rtex" or ".ranim" or ".rmesh" or ".raud" or ".rkey";
	}
}
