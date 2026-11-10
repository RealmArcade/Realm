using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Meta;

namespace Realm.Client.Services;

/// <summary>
///     What the editor has loaded for decals: the decoded asset per cache key, the packed ORM and normal maps drawn
///     from the same files, and the metadata lookups that decide which file a decal id names. The maps are keyed by
///     the pair of source textures they were packed from, so replacing one decal does not throw away the other's.
/// </summary>
public sealed class EditorDecalAssets
{
	public Dictionary<string, DecalAssetData> Assets { get; } = new();

	public Dictionary<(int, int), ImageTexture> OrmTextures { get; } = new();

	public Dictionary<string, ImageTexture> NormalTextures { get; } = new();

	public void Clear()
	{
		OrmTextures.Clear();
		NormalTextures.Clear();
		Assets.Clear();
	}

	public void Cache(string cacheKey, DecalAssetData assetData)
	{
		Assets[cacheKey] = assetData;
	}

	public DecalMetadata? FindMetadata(MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		return EditorDecalMetadata.FindDecalMetadata(metadata, decalId, filename, baseKey);
	}

	public VfxMetadata? FindVfxMetadata(MapMetadata metadata, string decalId, string filename, string baseKey)
	{
		return EditorDecalMetadata.FindVfxMetadata(metadata, decalId, filename, baseKey);
	}

	public (int cols, int rows, float fps, bool blend, string? explicitPath) ParseMetadata(string decalId, string filename, string baseKey, string wsPath)
	{
		return EditorDecalMetadata.ParseDecalMetadata(decalId, filename, baseKey, wsPath);
	}

	public List<string> CandidatePaths(string decalId, string? explicitTexturePath, string wsPath)
	{
		return EditorDecalMetadata.GetDecalCandidatePaths(decalId, explicitTexturePath, wsPath);
	}

	public void ParseRtexMetadata(string? customJson, ref int cols, ref int rows)
	{
		EditorDecalMetadata.ParseRtexDecalMetadata(customJson, ref cols, ref rows);
	}

	public void ParseExternalMetadata(string meta, ref int cols, ref int rows)
	{
		EditorDecalMetadata.ParseExternalDecalMetadata(meta, ref cols, ref rows);
	}

	/// <summary>Whether the caller supplied any frame layout of its own, which outranks the file's own metadata.</summary>
	public static bool HasOverrides(int overrideCols, int overrideRows, float overrideFps, bool? overrideSubframeBlend)
	{
		return overrideCols > 0 || overrideRows > 0 || overrideFps > 0.001f || overrideSubframeBlend.HasValue;
	}

	public string ResolveKeyFromMetadata(string decalId, string wsPath)
	{
		return EditorAssetImport.ResolveDecalKeyFromMetadata(decalId, wsPath);
	}

	public string FindFile(string targetKey, string wsPath)
	{
		return EditorAssetImport.FindDecalFile(targetKey, wsPath);
	}
}
