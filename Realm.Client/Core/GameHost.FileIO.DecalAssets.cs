using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Realm.Ecs;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;


public partial class GameHost
{
	private DecalAssetData? TryLoadDecalFromResource(string decalId, string cacheKey)
	{
		if (!decalId.StartsWith("res://")) return null;

		try
		{
			if (ResourceLoader.Exists(decalId))
			{
				var resTex = GD.Load<Texture2D>(decalId);
				var resData = new DecalAssetData
				{
					DecalId = decalId,
					TexturePath = decalId,
					PrimaryTexture = resTex,
					Columns = 1,
					Rows = 1
				};
				GameContext.EditorService.Decals.Cache(cacheKey, resData);
				return resData;
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"could not load the decal texture asset for {decalId}: {ex.GetType().Name}: {ex.Message}");
		}

		return null;
	}


	private bool LoadRtexDecalImages(string path, out Image? albedoImg, out Image? normalImg, ref int cols, ref int rows)
	{
		albedoImg = null;
		normalImg = null;
		byte[] rtexBytes = File.ReadAllBytes(path);
		var (customJson, layers, _) = RtexFile.Parse(rtexBytes);

		GameContext.EditorService.Decals.ParseRtexMetadata(customJson, ref cols, ref rows);

		albedoImg = LoadRtexDecalLayer(layers, 0);
		normalImg = LoadRtexDecalLayer(layers, 1);

		return albedoImg != null;
	}


	private Image? LoadRtexDecalLayer(IReadOnlyList<byte[]> layers, int layerIndex)
	{
		if (layers.Count <= layerIndex || layers[layerIndex] == null || layers[layerIndex].Length == 0)
			return null;

		var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		if (img.LoadWebpFromBuffer(layers[layerIndex]) != Error.Ok)
			img.LoadPngFromBuffer(layers[layerIndex]);

		return img;
	}

	private bool LoadExternalDecalImage(string path, out Image? albedoImg, ref int cols, ref int rows)
	{
		albedoImg = Image.LoadFromFile(path);
		string? meta = RealmMetadataHelper.ExtractMetadata(path);
		if (string.IsNullOrEmpty(meta)) return albedoImg != null;

		GameContext.EditorService.Decals.ParseExternalMetadata(meta, ref cols, ref rows);

		return albedoImg != null;
	}


	private DecalAssetData? TryLoadDecalImageFromPath(string path, string decalId, string? explicitTexturePath, string cacheKey, ref int detectedCols, ref int detectedRows)
	{
		try
		{
			Image? albedoImg = null;
			Image? normalImg = null;

			if (path.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				if (!LoadRtexDecalImages(path, out albedoImg, out normalImg, ref detectedCols, ref detectedRows)) return null;
			}
			else
			{
				if (!LoadExternalDecalImage(path, out albedoImg, ref detectedCols, ref detectedRows)) return null;
			}

			if (albedoImg != null)
			{
				var assetData = new DecalAssetData
				{
					DecalId = decalId,
					TexturePath = explicitTexturePath ?? decalId,
					Columns = 1,
					Rows = 1,
					Fps = 12.0f,
					SubframeBlend = false
				};

				if (!albedoImg.HasMipmaps()) albedoImg.GenerateMipmaps();
				assetData.PrimaryTexture = ImageTexture.CreateFromImage(albedoImg);

				if (normalImg != null)
				{
					if (!normalImg.HasMipmaps()) normalImg.GenerateMipmaps();
					assetData.PrimaryNormal = ImageTexture.CreateFromImage(normalImg);
				}

				GameContext.EditorService.Decals.Assets[cacheKey] = assetData;
				return assetData;
			}
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to load decal image from '{path}': {ex.Message}");
		}

		return null;
	}

	public DecalAssetData LoadDecalAsset(
		string decalId,
		int overrideCols = 0,
		int overrideRows = 0,
		float overrideFps = 0f,
		bool? overrideSubframeBlend = null,
		bool forceReload = false)
	{
		if (string.IsNullOrEmpty(decalId)) decalId = "logo";
		string cacheKey = decalId;

		if (!forceReload && !EditorDecalAssets.HasOverrides(overrideCols, overrideRows, overrideFps, overrideSubframeBlend))
		{
			if (TryGetValidCachedDecal(cacheKey, out var cachedData))
				return cachedData;
		}

		var resDecal = TryLoadDecalFromResource(decalId, cacheKey);
		if (resDecal != null) return resDecal;

		string filename = Path.GetFileName(decalId);
		string baseKey = Path.GetFileNameWithoutExtension(decalId);
		string wsPath = GameContext.EditorService._editingDirectory;

		var meta = GameContext.EditorService.Decals.ParseMetadata(decalId, filename, baseKey, wsPath);
		var loaded = TryLoadDecalFromCandidates(decalId, meta.cols, meta.rows, meta.explicitPath, wsPath, cacheKey);
		if (loaded != null) return loaded;

		return CreateFallbackDecal(decalId, meta.explicitPath, cacheKey);
	}


	private bool TryGetValidCachedDecal(string cacheKey, [NotNullWhen(true)] out DecalAssetData? cachedData)
	{
		cachedData = null;
		if (GameContext.EditorService.Decals.Assets.TryGetValue(cacheKey, out var data) && data != null && GodotObject.IsInstanceValid(data.PrimaryTexture))
		{
			cachedData = data;
			return true;
		}

		return false;
	}

	private DecalAssetData? TryLoadDecalFromCandidates(string decalId, int cols, int rows, string? explicitPath, string wsPath, string cacheKey)
	{
		var candidatePaths = GameContext.EditorService.Decals.CandidatePaths(decalId, explicitPath, wsPath);
		foreach (var path in candidatePaths)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

			int tempCols = cols, tempRows = rows;
			var loaded = TryLoadDecalImageFromPath(path, decalId, explicitPath, cacheKey, ref tempCols, ref tempRows);
			if (loaded != null) return loaded;
		}

		return null;
	}

	private DecalAssetData CreateFallbackDecal(string decalId, string? explicitPath, string cacheKey)
	{
		var fallback = new DecalAssetData
		{
			DecalId = decalId,
			TexturePath = explicitPath ?? decalId,
			PrimaryTexture = GD.Load<Texture2D>("res://icon.svg"),
			Columns = 1,
			Rows = 1
		};
		GameContext.EditorService.Decals.Assets[cacheKey] = fallback;
		return fallback;
	}

	public Texture2D LoadDecalTexture(string decalId)
	{
		return LoadDecalAsset(decalId).PrimaryTexture;
	}

	public string GetDecalTexturePath(string decalId)
	{
		if (string.IsNullOrEmpty(decalId)) decalId = "logo";
		if (decalId.StartsWith("res://")) return decalId;

		string wsPath = GameContext.EditorService._editingDirectory;
		string targetKey = GameContext.EditorService.Decals.ResolveKeyFromMetadata(decalId, wsPath);

		return GameContext.EditorService.Decals.FindFile(targetKey, wsPath);
	}
}
