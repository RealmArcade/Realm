using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;
using Realm.Shared.Services;

namespace Realm.Client.Services;

/// <summary>
///     Working out which decal or spritesheet a map's metadata describes, what frame grid it uses, and
///     which files on disk could hold its texture. All of it reads metadata.json and the workspace, so
///     the answers depend only on the arguments.
/// </summary>
public static class EditorDecalMetadata
{
	public static DecalMetadata? FindDecalMetadata(MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		if (metadata?.Decals == null) return null;
		if (metadata.Decals.TryGetValue(decalId, out var d0)) return d0;
		if (metadata.Decals.TryGetValue(filename, out var d1)) return d1;
		if (metadata.Decals.TryGetValue(baseKey, out var d2)) return d2;
		if (metadata.Decals.TryGetValue($"{baseKey}.rtex", out var d3)) return d3;
		if (metadata.Decals.TryGetValue($"{baseKey}.png", out var d4)) return d4;
		return null;
	}

	public static VfxMetadata? FindVfxMetadata(MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		if (metadata?.VfxSpritesheets == null) return null;
		if (metadata.VfxSpritesheets.TryGetValue(decalId, out var v0)) return v0;
		if (metadata.VfxSpritesheets.TryGetValue(filename, out var v1)) return v1;
		if (metadata.VfxSpritesheets.TryGetValue(baseKey, out var v2)) return v2;
		return null;
	}

	public static (int cols, int rows, float fps, bool blend, string? explicitPath) ParseDecalMetadata(string decalId, string filename, string baseKey, string wsPath)
	{
		int cols = 1, rows = 1;
		float fps = 12.0f;
		bool blend = true;
		string? explicitPath = null;

		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);
			if (metadata != null)
			{
				var meta = FindDecalMetadata(metadata, decalId, filename, baseKey);
				if (meta != null && !string.IsNullOrWhiteSpace(meta.TexturePath)) explicitPath = meta.TexturePath;

				var vmeta = FindVfxMetadata(metadata, decalId, filename, baseKey);
				if (vmeta != null)
				{
					if (vmeta.Columns > 0) cols = vmeta.Columns;
					if (vmeta.Rows > 0) rows = vmeta.Rows;
					if (vmeta.Fps > 0.001f) fps = vmeta.Fps;
					blend = vmeta.SubframeBlend;
				}
			}
		}
		catch
		{
		}

		return (cols, rows, fps, blend, explicitPath);
	}

	public static List<string> GetDecalCandidatePaths(string decalId, string? explicitTexturePath, string wsPath)
	{
		var candidatePaths = new List<string>();

		void AddCandidates(string pathOrName)
		{
			if (string.IsNullOrWhiteSpace(pathOrName)) return;
			string fName = Path.GetFileName(pathOrName);
			if (Path.IsPathRooted(pathOrName))
			{
				candidatePaths.Add(pathOrName);
				return;
			}

			candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", pathOrName));
			candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", fName));
			candidatePaths.Add(Path.Combine(wsPath, pathOrName));
			if (!fName.Contains('.'))
			{
				candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", fName + ".rtex"));
				candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", fName + ".webp"));
				candidatePaths.Add(Path.Combine(wsPath, "Assets", "decals", fName + ".png"));
			}
		}

		if (!string.IsNullOrEmpty(explicitTexturePath)) AddCandidates(explicitTexturePath);
		AddCandidates(decalId);
		return candidatePaths;
	}

	public static void ParseRtexDecalMetadata(string? customJson, ref int cols, ref int rows)
	{
		if (string.IsNullOrEmpty(customJson)) return;
		try
		{
			if (JsonNode.Parse(customJson) is not JsonObject jNode) return;

			if (TryGetPositiveIntFromJson(jNode, "columns", out int c))
				cols = c;

			if (TryGetPositiveIntFromJson(jNode, "rows", out int r))
				rows = r;
		}
		catch
		{
		}
	}

	public static void ParseExternalDecalMetadata(string meta, ref int cols, ref int rows)
	{
		if (string.IsNullOrEmpty(meta)) return;
		try
		{
			if (JsonNode.Parse(meta) is not JsonObject jNode) return;

			if (TryGetPositiveIntFromJson(jNode, "columns", out int c))
				cols = c;

			if (TryGetPositiveIntFromJson(jNode, "rows", out int r))
				rows = r;
		}
		catch
		{
		}
	}

	private static bool TryGetPositiveIntFromJson(JsonObject obj, string key, out int value)
	{
		value = 0;
		if (!obj.TryGetPropertyValue(key, out var node)) return false;
		if (node == null) return false;
		if (!int.TryParse(node.ToString(), out int parsed)) return false;
		if (parsed <= 0) return false;
		value = parsed;
		return true;
	}
}
