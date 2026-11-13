using System.Text.Json.Nodes;

namespace Realm.Shared.ModelOptimization;

/// <summary>reducing a generated lod set back to the master tier that carries it.</summary>
public static unsafe partial class GlbLodGenerator
	{
	private const string BaseTierNameSuffix = "_LOD0";
	private const string GeneratedTierNameMarker = "_LOD";
	private const string VisibilityRangeBeginKey = "visibility_range_begin";
	private const string VisibilityRangeEndKey = "visibility_range_end";

	public static bool HasGeneratedLodTiers(byte[] inputGlbBytes)
	{
		if (inputGlbBytes == null || inputGlbBytes.Length < 20) return false;

		var (jsonNode, _, _) = GlbManifestUtils.ParseGlb(inputGlbBytes);
		if (jsonNode is not JsonObject root) return false;
		if (root["meshes"] is not JsonArray meshes) return false;

		foreach (var meshNode in meshes)
		{
			if (meshNode is not JsonObject meshObj) continue;
			if (IsGeneratedTierName(meshObj["name"]?.GetValue<string>())) return true;
		}

		return false;
	}

	public static byte[] ReduceToBaseTier(byte[] inputGlbBytes)
	{
		if (inputGlbBytes == null || inputGlbBytes.Length < 20) return inputGlbBytes ?? Array.Empty<byte>();

		var (jsonNode, binBytes, version) = GlbManifestUtils.ParseGlb(inputGlbBytes);
		if (jsonNode is not JsonObject root) return inputGlbBytes;

		bool modified = GlbManifestUtils.RemoveAndRemapNodes(root, IsGeneratedTierNode);
		modified |= GlbManifestUtils.RemoveAndRemapMeshes(root, IsGeneratedTierMesh);
		modified |= RenameBaseTier(root);

		if (!modified) return inputGlbBytes;

		byte[] rebuiltGlbBytes = GlbManifestUtils.BuildGlb(root, binBytes, version);
		return GlbManifestUtils.CompactUnusedAccessors(rebuiltGlbBytes);
	}

	private static bool IsGeneratedTierMesh(JsonObject meshObj)
	{
		return IsGeneratedTierName(meshObj["name"]?.GetValue<string>());
	}

	private static bool IsGeneratedTierNode(JsonObject nodeObj)
	{
		return IsGeneratedTierName(nodeObj["name"]?.GetValue<string>());
	}

	private static bool IsGeneratedTierName(string? name)
	{
		if (string.IsNullOrEmpty(name)) return false;

		int markerIndex = name.LastIndexOf(GeneratedTierNameMarker, StringComparison.OrdinalIgnoreCase);
		if (markerIndex < 0) return false;

		ReadOnlySpan<char> tierText = name.AsSpan(markerIndex + GeneratedTierNameMarker.Length);
		if (tierText.Length == 0 || tierText.Length > 2) return false;
		for (int i = 0; i < tierText.Length; i++)
		{
			if (!char.IsAsciiDigit(tierText[i])) return false;
		}

		return !(tierText.Length == 1 && tierText[0] == '0');
	}

	private static bool RenameBaseTier(JsonObject root)
	{
		bool modified = RenameBaseTierMeshes(root);
		modified |= RenameBaseTierNodes(root);

		return modified;
	}

	private static bool RenameBaseTierMeshes(JsonObject root)
	{
		if (root["meshes"] is not JsonArray meshes) return false;

		bool modified = false;
		foreach (var meshNode in meshes)
		{
			if (meshNode is not JsonObject meshObj) continue;
			string? meshName = meshObj["name"]?.GetValue<string>();
			if (meshName == null || !meshName.EndsWith(BaseTierNameSuffix, StringComparison.OrdinalIgnoreCase)) continue;

			meshObj["name"] = meshName.Substring(0, meshName.Length - BaseTierNameSuffix.Length);
			modified = true;
		}

		return modified;
	}

	private static bool RenameBaseTierNodes(JsonObject root)
	{
		if (root["nodes"] is not JsonArray nodes) return false;

		bool modified = false;
		foreach (var nodeItem in nodes)
		{
			if (nodeItem is not JsonObject nodeObj) continue;

			string? nodeName = nodeObj["name"]?.GetValue<string>();
			if (nodeName != null && nodeName.EndsWith(BaseTierNameSuffix, StringComparison.OrdinalIgnoreCase))
			{
				nodeObj["name"] = nodeName.Substring(0, nodeName.Length - BaseTierNameSuffix.Length);
				modified = true;
			}

			modified |= RemoveVisibilityRange(nodeObj);
		}

		return modified;
	}

	private static bool RemoveVisibilityRange(JsonObject nodeObj)
	{
		if (nodeObj["extras"] is not JsonObject extras) return false;

		bool modified = extras.Remove(VisibilityRangeBeginKey);
		modified |= extras.Remove(VisibilityRangeEndKey);

		if (extras.Count == 0)
		{
			nodeObj.Remove("extras");
		}

		return modified;
	}
	}
