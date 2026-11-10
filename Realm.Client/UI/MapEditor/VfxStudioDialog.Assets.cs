using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Client.VFX;

namespace Realm.Client.UI.MapEditor;

public partial class VfxStudioDialog
{
	private List<string> ScanTextureAssets()
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var spritesheetFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var spritesheetTemplateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		ScanTextureFolders(wsPath, results, spritesheetFileNames);
		ScanTextureAssetsObj(wsPath, results, spritesheetFileNames);
		ScanSpritesheetTemplates(wsPath, spritesheetFileNames, spritesheetTemplateIds);

		results.RemoveWhere(asset => spritesheetFileNames.Contains(Path.GetFileName(asset)));
		results.UnionWith(spritesheetTemplateIds);

		return results.OrderBy(asset => asset, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void ScanTextureFolders(string wsPath, HashSet<string> results, HashSet<string> spritesheetFileNames)
	{
		string[] subFolders = { "ribbons", "decals", "textures", "vfx", "vfx_spritesheets", "vfx_radial", "vfx_vertical", "icons", "skyboxes", "noise" };
		foreach (var subFolder in subFolders)
		{
			string dir = Path.Combine(wsPath, "Assets", subFolder);
			if (!Directory.Exists(dir)) continue;

			bool isSpritesheetFolder = subFolder == "vfx_spritesheets";
			HashSet<string> target = isSpritesheetFolder ? spritesheetFileNames : results;

			foreach (var file in Directory.GetFiles(dir, "*.*"))
			{
				if (file.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
				    file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
				    file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
				{
					target.Add(Path.GetFileName(file));
				}
			}
		}
	}

	private void ScanTextureAssetsObj(string wsPath, HashSet<string> results, HashSet<string> spritesheetFileNames)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj == null) return;

			string[] categories = { "Ribbon", "Decal", "Terrain", "Spritesheet", "Noise", "Icon", "Skybox", "vfx_radial", "vfx_vertical" };
			foreach (var catName in categories)
			{
				var catDict = assetsObj.GetCategory(catName);
				if (catDict == null) continue;

				HashSet<string> target = catName == "Spritesheet" ? spritesheetFileNames : results;

				foreach (var kvp in catDict)
				{
					if (!string.IsNullOrEmpty(kvp.Key))
					{
						target.Add(Path.GetFileName(kvp.Key));
					}
				}
			}
		}
		catch
		{
		}
	}

	private void ScanSpritesheetTemplates(string wsPath, HashSet<string> spritesheetFileNames, HashSet<string> spritesheetTemplateIds)
	{
		var coveredTextureFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var metadata) || metadata?.VfxSpritesheets == null)
		{
			AddDerivedSpritesheetTemplateIds(spritesheetFileNames, coveredTextureFileNames, spritesheetTemplateIds);
			return;
		}

		foreach (var kvp in metadata.VfxSpritesheets)
		{
			if (string.IsNullOrWhiteSpace(kvp.Key) || string.IsNullOrWhiteSpace(kvp.Value?.TexturePath)) continue;

			spritesheetTemplateIds.Add(kvp.Key);
			string textureFileName = Path.GetFileName(kvp.Value.TexturePath);
			spritesheetFileNames.Add(textureFileName);
			coveredTextureFileNames.Add(textureFileName);
		}

		AddDerivedSpritesheetTemplateIds(spritesheetFileNames, coveredTextureFileNames, spritesheetTemplateIds);
	}

	private void AddDerivedSpritesheetTemplateIds(HashSet<string> spritesheetFileNames, HashSet<string> coveredTextureFileNames, HashSet<string> spritesheetTemplateIds)
	{
		foreach (var fileName in spritesheetFileNames)
		{
			if (coveredTextureFileNames.Contains(fileName)) continue;

			string slug = TemplateIDHelper.GenerateSlug(fileName);
			spritesheetTemplateIds.Add(TemplateIDHelper.NormalizeTemplateID("spritesheet", slug));
		}
	}


	private List<string> ScanNoiseAssets()
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = GameContext.EditorService._editingDirectory;

		ScanNoiseFolder(wsPath, results);
		ScanNoiseAssetsObj(wsPath, results);

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void ScanNoiseFolder(string wsPath, HashSet<string> results)
	{
		string dir = Path.Combine(wsPath, "Assets", "noise");
		if (!Directory.Exists(dir)) return;

		foreach (var file in Directory.GetFiles(dir, "*.*"))
		{
			if (file.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
			    file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
			    file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
			{
				results.Add(Path.GetFileName(file));
			}
		}
	}

	private void ScanNoiseAssetsObj(string wsPath, HashSet<string> results)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			var noiseDict = assetsObj?.GetCategory("Noise");

			if (noiseDict == null) return;

			foreach (var kvp in noiseDict)
			{
				if (!string.IsNullOrEmpty(kvp.Key))
				{
					results.Add(Path.GetFileName(kvp.Key));
				}
			}
		}
		catch
		{
		}
	}


	private List<string> ScanProjectileMeshAssets()
	{
		var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = GameContext.EditorService._editingDirectory;

		ScanProjectileAssetsObj(wsPath, results);

		void ScanFolder(string folderPath)
		{
			if (!Directory.Exists(folderPath)) return;

			foreach (var file in Directory.GetFiles(folderPath, "*.rmesh"))
			{
				results.Add(Path.GetFileName(file));
			}
		}

		ScanFolder(Path.Combine(wsPath, "Assets", "models", "projectiles"));
		ScanFolder(Path.Combine(wsPath, "Assets", "models", "props"));

		string templateDir = PathUtils.FindPath("MapTemplate/Assets/models/projectiles");
		if (!string.IsNullOrEmpty(templateDir)) ScanFolder(templateDir);

		return results.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private void ScanProjectileAssetsObj(string wsPath, HashSet<string> results)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj == null) return;

			string[] categories = { "Character", "Building", "Prop", "Item" };
			foreach (var catName in categories)
			{
				var catDict = assetsObj.GetCategory(catName);
				if (catDict == null) continue;

				foreach (var modelProp in catDict)
				{
					string fn = modelProp.Key;
					if (!string.IsNullOrEmpty(fn) && fn.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
					{
						results.Add(Path.GetFileName(fn));
					}
				}
			}
		}
		catch
		{
		}
	}


	private void SaveSpritesheetGrid(string key, int columns, int rows, float fps = 20.0f, bool subframeBlend = true)
	{
		string wsPath = GameContext.EditorService._editingDirectory;

		try
		{
			string resolvedTexturePath = VfxShaderManager.ResolveSpritesheetTexturePath(key);
			string fileName = Path.GetFileName(resolvedTexturePath);
			if (!fileName.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) &&
			    !fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
			    !fileName.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
			{
				fileName += ".rtex";
			}

			string slug = TemplateIDHelper.GenerateSlug(fileName);
			string spritesheetTemplateId = TemplateIDHelper.NormalizeTemplateID("spritesheet", slug);
			GameContext.MetadataService.UpdateMetadata(wsPath, m =>
			{
				m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
				m.VfxSpritesheets[spritesheetTemplateId] = new VfxMetadata
				{
					TexturePath = fileName,
					Columns = columns,
					Rows = rows,
					Fps = fps,
					SubframeBlend = subframeBlend
				};
			});
		}
		catch (Exception ex)
		{
			GameLog.LogError($"[VfxStudioDialog] SaveSpritesheetGrid error: {ex.Message}");
		}
	}

	private void CopyScriptCode()
	{
		string code = BuildScriptCode();
		DisplayServer.ClipboardSet(code);
		Hud?.ShowFeedback(TranslationServer.Translate("Copied script code to clipboard"));
	}

	private string BuildScriptCode()
	{
		SyncConfigFromControls();

		string effectId = !string.IsNullOrEmpty(_currentConfig.VfxId) ? _currentConfig.VfxId : _vfxId;
		string colorArgument = FormatScriptColor(_currentConfig.BaseColor);

		switch (_lastPreviewKind)
		{
			case VfxPreviewKind.Shockwave:
				return $"api.SpawnGroundShockwave(targetPos, new System.Numerics.Vector3(0f, 0f, -1f), {FormatScriptFloat(ShockwavePreviewRadius)}, {FormatScriptFloat(ShockwavePreviewSpeed)}, {FormatScriptFloat(ShockwavePreviewDuration)}, {colorArgument});";
			case VfxPreviewKind.GroundRing:
				return $"api.SpawnExpandingGroundRing(targetPos, {FormatScriptFloat(RingPreviewRadius)}, {FormatScriptFloat(RingPreviewSpeed)}, {FormatScriptFloat(RingPreviewDuration)}, {colorArgument});";
			case VfxPreviewKind.BurstSphere:
				return $"api.SpawnExpandingBurstSphere(targetPos, {FormatScriptFloat(BurstPreviewRadius)}, {FormatScriptFloat(BurstPreviewSpeed)}, {FormatScriptFloat(BurstPreviewDuration)}, {colorArgument});";
			case VfxPreviewKind.ChainBeam:
				return $"System.Numerics.Vector3[] chainPoints = {FormatScriptPoints(ChainPreviewPoints)};\napi.SpawnChainBeam(\"{effectId}\", chainPoints, {FormatScriptFloat(ChainPreviewJumpDelay)}, {ChainPreviewForkCount}, {FormatScriptFloat(ChainPreviewFadeLifetime)}, {FormatScriptFloat(ChainPreviewWidth)}, {colorArgument});";
			case VfxPreviewKind.BarrageVolley:
				return $"api.SpawnBarrageVolley(\"{effectId}\", targetPos, {FormatScriptFloat(BarragePreviewRadius)}, {BarragePreviewCount}, {FormatScriptFloat(BarragePreviewInterval)});";
			default:
				if (_currentConfig.PrimitiveType == VfxPrimitiveType.AuraCapsule || _currentConfig.PrimitiveType == VfxPrimitiveType.HemisphereDome)
				{
					return $"api.AttachPersistentAura(unit, \"{effectId}\", 1.0f);";
				}

				return $"api.SpawnVisualEffect(\"{effectId}\", targetPos, 1.0f);";
		}
	}

	private static string FormatScriptFloat(float value)
	{
		return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "f";
	}

	private static string FormatScriptColor(string colorHex)
	{
		Color color = VfxShaderManager.ParseColorSafe(colorHex, Colors.Orange);
		return $"new System.Numerics.Vector3({FormatScriptFloat(color.R)}, {FormatScriptFloat(color.G)}, {FormatScriptFloat(color.B)})";
	}

	private static string FormatScriptPoints(Vector3[] points)
	{
		var formattedPoints = points.Select(point => $"new System.Numerics.Vector3({FormatScriptFloat(point.X)}, {FormatScriptFloat(point.Y)}, {FormatScriptFloat(point.Z)})");
		return "new System.Numerics.Vector3[] { " + string.Join(", ", formattedPoints) + " }";
	}

}
