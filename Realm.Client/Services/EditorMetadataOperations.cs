using System;
using System.Collections.Generic;
using System.IO;
using Realm.Client.Core;
using Realm.Client.Utils;
using Realm.Ecs.Components.Terrain;
using Realm.Shared.Metadata;
using Realm.Shared.Services;
using Realm.Shared.Textures;

namespace Realm.Client.Services;

/// <summary>
///     Writing an imported asset into the map's metadata.json: it adds or updates the matching unit, building, prop,
///     resource or texture entry, records the auto-computed offset and scale, and refreshes the manifest hash. The
///     workspace is passed in, so the writer itself holds no editor state.
/// </summary>
internal static class EditorMetadataOperations
{
	internal static void UpdateMetadataJsonAsset(string category, string fileName, string blake3Hash, string wsPath,
		string? subCategory = null, int columns = 0, int rows = 0, int targetSlot = -1)
	{
		try
		{
			var metadata = MapFileService.LoadMetadata(wsPath);

			if (category == "glb" && GameContext.Host != null)
			{
				string normKey = GameContext.EditorService.NormalizeModelAssetKey(fileName);
				if (!GameContext.ModelOverrideService.ModelObstacleRadii.ContainsKey(normKey))
				{
					string modelPath = Path.Combine(wsPath, "Assets", "models", subCategory ?? "", fileName);
					float radius = GameContext.Host.MeasureModelRadiusFromFile(modelPath);
					if (radius > 0f)
					{
						float rounded = (float)Math.Round(radius, 2);
						GameContext.ModelOverrideService.ModelObstacleRadii[normKey] = rounded;
					}
				}
			}

			if (!string.IsNullOrEmpty(subCategory) && category == "glb")
			{
				float defaultScale = subCategory.ToLowerInvariant() switch
				{
					"resources" => 2.75f,
					"buildings" => 1.5f,
					"props" => 1.25f,
					"units" => 1.0f,
					_ => 1.0f
				};

				string modelFullPath = Path.Combine(wsPath, "Assets", "models", subCategory, fileName);
				if (!File.Exists(modelFullPath))
				{
					modelFullPath = Path.Combine(wsPath, "Assets", "glb", subCategory, fileName);
				}

				var (minY, autoYOffset) = ModelCache.CalculateModelBounds(modelFullPath, defaultScale);
				string subLower = subCategory.ToLowerInvariant();
				bool isPropOrRes = subLower == "props" || subLower == "resources" || subLower == "attachments" || subLower == "weapons" || subLower == "items" || subLower == "projectiles";

				GameContext.Host?.SetModelYOffset(fileName, autoYOffset);
				GameContext.Host?.SetModelScale(fileName, defaultScale);

				string unitId = Path.GetFileNameWithoutExtension(fileName);
				if (subLower == "buildings")
				{
					var b = metadata.FindBuilding(unitId);
					if (b == null)
					{
						b = new UnitMetadata
						{
							TemplateID = unitId,
							Name = unitId,
							Scale = defaultScale,
							YOffset = autoYOffset,
							PathingType = (int)TerrainPathingFlags.Buildable,
							ModelPath = fileName,
							NormalizeLuminance = true,
							IgnorePlayerColor = isPropOrRes
						};
						metadata.AddOrUpdateBuilding(b);
					}
					else if (autoYOffset != 0f)
					{
						b.YOffset = autoYOffset;
					}
				}
				else if (subLower == "props")
				{
					var p = metadata.FindProp(unitId);
					if (p == null)
					{
						p = new PropMetadata
						{
							TemplateID = unitId,
							Name = unitId,
							Scale = defaultScale,
							YOffset = autoYOffset,
							PathingType = 0xFF,
							ModelPath = fileName,
							NormalizeLuminance = true,
							IgnorePlayerColor = isPropOrRes
						};
						metadata.AddOrUpdateProp(p);
					}
					else if (autoYOffset != 0f)
					{
						p.YOffset = autoYOffset;
					}
				}
				else if (subLower == "resources")
				{
					var r = metadata.FindResource(unitId);
					if (r == null)
					{
						r = new ResourceMetadata
						{
							TemplateID = unitId,
							Name = unitId,
							Scale = defaultScale,
							YOffset = autoYOffset,
							PathingType = 0xFF,
							ModelPath = fileName,
							NormalizeLuminance = true,
							IgnorePlayerColor = isPropOrRes
						};
						metadata.AddOrUpdateResource(r);
					}
					else if (autoYOffset != 0f)
					{
						r.YOffset = autoYOffset;
					}
				}
				else
				{
					var u = metadata.FindUnit(unitId);
					if (u == null)
					{
						u = new UnitMetadata
						{
							TemplateID = unitId,
							Name = unitId,
							Scale = defaultScale,
							YOffset = autoYOffset,
							PathingType = (int)(TerrainPathingFlags.Ground | TerrainPathingFlags.ShallowWater),
							ModelPath = fileName,
							NormalizeLuminance = true,
							IgnorePlayerColor = isPropOrRes
						};
						metadata.AddOrUpdateUnit(u);
					}
					else if (autoYOffset != 0f)
					{
						u.YOffset = autoYOffset;
					}
				}
			}

			if (category == "textures" || category == "terrain" || category == "Terrain")
			{
				string slug = TemplateIDHelper.GenerateSlug(fileName);
				string terrainTemplateId = TemplateIDHelper.NormalizeTemplateID("terrain", slug);
				int swatchIdx = targetSlot >= 0 ? targetSlot : 0;
				metadata.Textures ??= new System.Collections.Generic.Dictionary<string, TextureMetadata>(StringComparer.OrdinalIgnoreCase);
				if (!metadata.Textures.TryGetValue(terrainTemplateId, out var texMeta) || texMeta == null)
				{
					if (!metadata.Textures.TryGetValue(fileName, out texMeta) || texMeta == null)
					{
						texMeta = new TextureMetadata();
					}
					else
					{
						metadata.Textures.Remove(fileName);
					}

					metadata.Textures[terrainTemplateId] = texMeta;
				}

				texMeta.TexturePath = fileName;
				texMeta.SwatchIndex = swatchIdx;

				string texPath = Path.Combine(wsPath, "Assets", "textures", fileName);
				if (File.Exists(texPath))
				{
					texMeta.ScaleFactor = TextureConverter.CalculateLuminanceScaleFactor(texPath);
				}
			}

			MapFileService.SaveMetadata(wsPath, metadata);
			MapAssetHelper.UpdateManifestAsset(wsPath, category, fileName, blake3Hash);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to update metadata.json asset: {ex.Message}");
		}
	}
}
