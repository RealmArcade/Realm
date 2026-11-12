using System;
using System.Collections.Generic;
using Godot;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

/// <summary>
/// Builds the procedural spawn descriptors a terrain cell asks for: the decals and VFX that a water
/// profile or a terrain texture scatters across it. Nothing here touches editor state; the cell and the
/// profile are the whole input.
/// </summary>
public static class EditorProceduralSpawns
{
	public static List<ProceduralSpawnDescriptor> BuildWater(int x, int z, byte profileIndex, float waterY, float quadSize, int width, int depth)
	{
		var descriptors = new List<ProceduralSpawnDescriptor>();
		CollectWater(x, z, GameContext.Terrain?.GetWaterProfile(profileIndex), waterY, quadSize, width, depth, descriptors);
		return descriptors;
	}

	public static void CollectWater(int x, int z, WaterProfileSaveData? profile, float waterY, float quadSize, int width, int depth, List<ProceduralSpawnDescriptor> descriptors)
	{
		if (profile == null) return;

		float cellCenterX = (x + 0.5f - width / 2.0f) * quadSize;
		float cellCenterZ = (z + 0.5f - depth / 2.0f) * quadSize;

		CollectBombingSpawns(profile.DecalBombingRules, ProceduralSpawnKind.Decal, r => r.DecalId, r => r.Density, r => r.MinScale, r => r.MaxScale, cellCenterX, cellCenterZ, waterY, quadSize, descriptors);
		CollectBombingSpawns(profile.VfxBombingRules, ProceduralSpawnKind.Vfx, r => r.VfxId, r => r.Density, r => r.MinScale, r => r.MaxScale, cellCenterX, cellCenterZ, waterY, quadSize, descriptors);
	}

	public static List<ProceduralSpawnDescriptor> BuildTerrain(int x, int z, int textureIndex, float terrainY, float quadSize, int width, int depth)
	{
		var descriptors = new List<ProceduralSpawnDescriptor>();

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		if (string.IsNullOrEmpty(wsPath)) return descriptors;
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaRoot) || metaRoot == null) return descriptors;

		string? swatchName = null;
		if (GameContext.Host?.GroundTerrain != null && textureIndex >= 0 && textureIndex < GameContext.Host.GroundTerrain.LoadedTextureList.Count)
		{
			swatchName = GameContext.Host.GroundTerrain.LoadedTextureList[textureIndex];
		}

		if (string.IsNullOrEmpty(swatchName)) return descriptors;

		var prof = metaRoot.GetTerrainProfile(swatchName);
		if (prof == null) return descriptors;

		float cellCenterX = (x + 0.5f - width / 2.0f) * quadSize;
		float cellCenterZ = (z + 0.5f - depth / 2.0f) * quadSize;

		CollectBombingSpawns(prof.DecalBombingRules, ProceduralSpawnKind.Decal, r => r.DecalId, r => r.Density, r => r.MinScale, r => r.MaxScale, cellCenterX, cellCenterZ, terrainY, quadSize, descriptors);
		CollectBombingSpawns(prof.VfxBombingRules, ProceduralSpawnKind.Vfx, r => r.VfxId, r => r.Density, r => r.MinScale, r => r.MaxScale, cellCenterX, cellCenterZ, terrainY, quadSize, descriptors);

		return descriptors;
	}

	private static void CollectBombingSpawns<T>(List<T>? rules, ProceduralSpawnKind kind, Func<T, string> getId, Func<T, float> getDensity, Func<T, float> getMinScale, Func<T, float> getMaxScale, float cellCenterX, float cellCenterZ, float baseY, float quadSize, List<ProceduralSpawnDescriptor> descriptors)
	{
		if (rules == null || rules.Count == 0) return;

		foreach (var rule in rules)
		{
			string id = getId(rule);
			if (string.IsNullOrWhiteSpace(id)) continue;
			if (Random.Shared.NextSingle() > (getDensity(rule) * 0.05f)) continue;

			float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
			float scale = Mathf.Lerp(getMinScale(rule), getMaxScale(rule), Random.Shared.NextSingle());
			float rotY = Random.Shared.NextSingle() * 360f;
			var pos = new Vector3(cellCenterX + jx, baseY, cellCenterZ + jz);

			descriptors.Add(new ProceduralSpawnDescriptor(kind, id, pos, rotY, scale));
		}
	}
}
