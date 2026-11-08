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

public static partial class RealmMetadataHelper
	{
	public const string AssetAgreementWarning = "The Realm Platform UGC Agreement states that files cannot be used outside the Realm Platform unless you are the original author of the asset. Do you understand?";
	public const string MixamoLicensingWarning = "Export not available due to licensing. Download animations from https://www.mixamo.com";

	public static bool SupportsMetadata(string extensionOrPath)
	{
		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
		if (ext is ".rtex" or ".ranim" or ".rmesh" or ".raud" or ".rkey") return true;

		if (!File.Exists(extensionOrPath)) return false;

		return CheckFileMagicBytes(extensionOrPath);
	}

	private static bool CheckFileMagicBytes(string filePath)
	{
		try
		{
			Span<byte> magic = stackalloc byte[4];
			using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			if (fs.Read(magic) != 4) return false;

			return magic.SequenceEqual(RmeshFile.MagicBytes) ||
			       magic.SequenceEqual(RtexFile.MagicBytes) ||
			       magic.SequenceEqual(RanimFile.MagicBytes) ||
			       magic.SequenceEqual(RaudFile.MagicBytes) ||
			       magic.SequenceEqual(RkeyFile.MagicBytes);
		}
		catch
		{
			return false;
		}
	}

}
