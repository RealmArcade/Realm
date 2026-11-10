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

/// <summary>stripping metadata for the asset hash.</summary>
public static partial class RealmMetadataHelper
	{
	public static byte[] StripMetadataEphemeral(byte[] bytes, string? extensionOrPath = null)
	{
		if (bytes == null || bytes.Length == 0) return bytes ?? Array.Empty<byte>();

		string extension = GetExtensionOrDefault(extensionOrPath);

		return TryStripFormatMetadata(extension, bytes);
	}

	private static string GetExtensionOrDefault(string? extensionOrPath)
	{
		if (string.IsNullOrEmpty(extensionOrPath)) return string.Empty;
		return Path.GetExtension(extensionOrPath).ToLowerInvariant();
	}

	private static byte[] TryStripFormatMetadata(string extension, byte[] bytes)
	{
		try
		{
			if (ShouldProcessFormat(extension, bytes, ".rmesh", RmeshFile.IsRmeshBytes)) return StripFormatMetadata(bytes, RmeshFile.Magic, (b, m) => RealmContainerFile.SetContainerMetadata(b, RmeshFile.Magic, RmeshFile.Name, RmeshFile.Format, m));
			if (ShouldProcessFormat(extension, bytes, ".raud", RaudFile.IsRaudBytes)) return StripFormatMetadata(bytes, RaudFile.Magic, (b, m) => RealmContainerFile.SetContainerMetadata(b, RaudFile.Magic, RaudFile.Name, RaudFile.Format, m));
			if (ShouldProcessFormat(extension, bytes, ".rtex", RtexFile.IsRtexBytes)) return StripFormatMetadata(bytes, RtexFile.Magic, (b, m) => RealmContainerFile.SetContainerMetadata(b, RtexFile.Magic, RtexFile.Name, RtexFile.Format, m));
			if (ShouldProcessFormat(extension, bytes, ".ranim", RanimFile.IsRanimBytes)) return StripFormatMetadata(bytes, RanimFile.Magic, (b, m) => RealmContainerFile.SetContainerMetadata(b, RanimFile.Magic, RanimFile.Name, RanimFile.Format, m, configureMetadata: metaObj => metaObj[AssetMetadataKeys.AssetType] ??= AssetType.Animation.ToCanonicalString()));
			if (ShouldProcessFormat(extension, bytes, ".rkey", RkeyFile.IsRkeyBytes)) return StripFormatMetadata(bytes, RkeyFile.Magic, (b, m) => RealmContainerHeader.SetMetadata(b, RkeyFile.Magic, m, RkeyFile.Name));
		}
		catch
		{
		}

		return bytes;
	}


	private static bool ShouldProcessFormat(string extension, byte[] bytes, string formatExt, IsFormatBytesDelegate isFormatBytes)
	{
		return extension == formatExt || ((string.IsNullOrEmpty(extension) || extension == ".bin") && isFormatBytes(bytes));
	}

	private static byte[] StripFormatMetadata(byte[] bytes, ReadOnlySpan<byte> magic, SetMetadataDelegate setMetadata)
	{
		if (RealmContainerHeader.TryReadHeader(bytes, magic, out _, out _, out int payloadOffset))
		{
			return bytes.AsSpan(payloadOffset).ToArray();
		}

		return setMetadata(bytes, null);
	}
}
