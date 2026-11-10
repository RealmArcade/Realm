using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Services;

namespace Realm.Client.UI.MapEditor;

public partial class TemplateManagerDialog
{
	private void OpenEditDialogForObject(ObjectItemInfo item)
	{
		_currentPreviewTemplateID = item.TemplateID;
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		switch (item.Category)
		{
			case "units" or "buildings" or "resources" or "props":
			case "upgrades" or "items":
				OpenDefaultEditDialog(item);
				break;
			case "weapons":
				OpenWeaponEditDialog(item, wsPath);
				break;
			case "abilities":
				OpenAbilityEditDialog(item, wsPath);
				break;
			case "terrain":
				OpenTerrainEditDialog(item, wsPath);
				break;
			case "spritesheets":
				OpenSpritesheetEditDialog(item, wsPath);
				break;
			case "decals":
				OpenDecalEditDialog(item, wsPath);
				break;
			case "shaders" or "shader" or "spawnshader" or "spawnshaders":
				OpenShaderEditDialog(item, wsPath);
				break;
			case "vfx":
				OpenVfxEditDialog(item, wsPath);
				break;
			default:
				OpenDefaultEditDialog(item);
				break;
		}
	}

	private void OpenWeaponEditDialog(ObjectItemInfo item, string wsPath)
	{
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) && meta?.Templates?.Weapons != null)
		{
			int wIdx = meta.Templates.Weapons.FindIndex(x => string.Equals(x.TemplateID, item.TemplateID, StringComparison.OrdinalIgnoreCase));
			if (wIdx >= 0)
			{
				var w = meta.Templates.Weapons[wIdx];
				_weaponVfxDialog.OpenForWeapon(w.TemplateID, w, updatedWeapon =>
				{
					GameContext.MetadataService.UpdateMetadata(wsPath, m =>
					{
						if (!string.Equals(item.TemplateID, updatedWeapon.TemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(item.TemplateID))
						{
							m.RemoveWeapon(item.TemplateID);
						}

						m.AddOrUpdateWeapon(updatedWeapon);
					});
					_currentPreviewTemplateID = updatedWeapon.TemplateID;
					RefreshObjectList();
				});
				_weaponVfxDialog.SetRandomizeAssetsAction(() =>
				{
					_weaponVfxDialog.RandomizeAssets();
					RefreshObjectList();
				});
			}
		}
	}

	private void OpenAbilityEditDialog(ObjectItemInfo item, string wsPath)
	{
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaAbi) || metaAbi?.Templates?.Abilities == null) return;

		var a = metaAbi.Templates.Abilities.FirstOrDefault(x => string.Equals(x.TemplateID, item.TemplateID, StringComparison.OrdinalIgnoreCase));
		if (a == null) return;

		var jsonObj = new JsonObject
		{
			["TemplateID"] = a.TemplateID,
			["AbilityId"] = a.TemplateID,
			["Name"] = a.Name,
			["VisualEffect"] = a.VisualEffect,
			["CastSound"] = a.CastSound,
			["IconPath"] = a.IconPath,
			["AreaOfEffectRadius"] = a.AreaOfEffectRadius
		};

		_abilityVfxDialog.OpenForAbility(a.TemplateID, jsonObj, updatedObj => HandleAbilityUpdate(item.TemplateID, a, updatedObj, wsPath));
		_abilityVfxDialog.SetRandomizeAssetsAction(() =>
		{
			_abilityVfxDialog.RandomizeAssets();
			RefreshObjectList();
		});
	}

	private void HandleAbilityUpdate(string oldTemplateID, AbilityMetadata a, JsonObject updatedObj, string wsPath)
	{
		string newAbilityId = GetNewAbilityId(updatedObj, oldTemplateID);
		bool idChanged = !string.Equals(oldTemplateID, newAbilityId, StringComparison.OrdinalIgnoreCase);
		bool hasOldId = !string.IsNullOrEmpty(oldTemplateID);

		GameContext.MetadataService.UpdateMetadata(wsPath, m =>
		{
			if (idChanged && hasOldId)
			{
				m.RemoveAbility(oldTemplateID);
			}

			UpdateAbilityMetadata(a, updatedObj, newAbilityId);
			m.AddOrUpdateAbility(a);
		});
		_currentPreviewTemplateID = newAbilityId;
		RefreshObjectList();
	}

	private string GetNewAbilityId(JsonObject updatedObj, string oldTemplateID)
	{
		if (updatedObj.TryGetPropertyValue("TemplateID", out var tNode))
		{
			if (tNode != null && !string.IsNullOrEmpty(tNode.ToString())) return tNode.ToString();
		}

		if (updatedObj.TryGetPropertyValue("AbilityId", out var aNode))
		{
			if (aNode != null && !string.IsNullOrEmpty(aNode.ToString())) return aNode.ToString();
		}

		return oldTemplateID;
	}

	private void UpdateAbilityMetadata(AbilityMetadata a, JsonObject updatedObj, string newAbilityId)
	{
		a.TemplateID = newAbilityId;

		if (updatedObj.TryGetPropertyValue("VisualEffect", out var vNode)) a.VisualEffect = vNode?.ToString();
		if (updatedObj.TryGetPropertyValue("CastSound", out var cNode)) a.CastSound = cNode?.ToString();
		if (updatedObj.TryGetPropertyValue("IconPath", out var iNode)) a.IconPath = iNode?.ToString();

		if (updatedObj.TryGetPropertyValue("AreaOfEffectRadius", out var rNode) && float.TryParse(rNode?.ToString(), out float radius))
		{
			a.AreaOfEffectRadius = radius;
		}
	}

	private void OpenTerrainEditDialog(ObjectItemInfo item, string wsPath)
	{
		JsonObject curTerData = new JsonObject();
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaTer) && metaTer?.Textures != null)
		{
			TextureMetadata? tNode = FindTextureMetadata(metaTer, item);
			if (tNode != null)
			{
				curTerData = JsonSerializer.SerializeToNode(tNode) as JsonObject ?? new JsonObject();
			}
		}

		_textureEditDialog.OpenForTexture(item.TemplateID, curTerData, updatedObj =>
		{
			string newId = updatedObj?["TemplateID"]?.ToString() ?? item.TemplateID;
			_currentPreviewTemplateID = newId;
			RefreshObjectList();
		});
		_textureEditDialog.SetRandomizeAssetsAction(() =>
		{
			_textureEditDialog.RandomizeAssets();
			RefreshObjectList();
		});
	}

	private TextureMetadata? FindTextureMetadata(MapMetadata metaTer, ObjectItemInfo item)
	{
		if (metaTer.Textures.TryGetValue(item.TemplateID, out var tNode)) return tNode;
		var (_, slug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
		if (!string.IsNullOrEmpty(slug) && metaTer.Textures.TryGetValue(slug, out tNode)) return tNode;
		if (!string.IsNullOrEmpty(item.ModelPath) && metaTer.Textures.TryGetValue(item.ModelPath, out tNode)) return tNode;
		if (!string.IsNullOrEmpty(slug) && metaTer.Textures.TryGetValue($"{slug}.rtex", out tNode)) return tNode;
		if (!string.IsNullOrEmpty(slug))
		{
			var matchKvp = metaTer.Textures.FirstOrDefault(k => string.Equals(Path.GetFileNameWithoutExtension(k.Key), slug, StringComparison.OrdinalIgnoreCase));
			return matchKvp.Value;
		}

		return null;
	}

	private void OpenSpritesheetEditDialog(ObjectItemInfo item, string wsPath)
	{
		int initCols = 1, initRows = 1;
		float initFps = 20.0f;
		bool initBlend = true;
		string initRtex = "";

		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaSpr) && metaSpr?.VfxSpritesheets != null)
		{
			VfxMetadata? sNode = FindSpritesheetMetadata(metaSpr, item);
			if (sNode != null)
			{
				initCols = sNode.Columns > 0 ? sNode.Columns : 1;
				initRows = sNode.Rows > 0 ? sNode.Rows : 1;
				initFps = sNode.Fps > 0 ? sNode.Fps : 20.0f;
				initBlend = sNode.SubframeBlend;
				initRtex = sNode.TexturePath ?? "";
			}
		}

		_spritesheetEditDialog.OpenForSheet(item.TemplateID, initRtex, initCols, initRows, initFps, initBlend, (newId, rtex, cols, rows, fps, blend) =>
		{
			UpdateSpritesheetMetadata(wsPath, item, newId, rtex, cols, rows, fps, blend);
			_currentPreviewTemplateID = newId;
			RefreshObjectList();
		});
		_spritesheetEditDialog.SetRandomizeAssetsAction(() =>
		{
			_spritesheetEditDialog.RandomizeAssets();
			RefreshObjectList();
		});
	}

	private VfxMetadata? FindSpritesheetMetadata(MapMetadata metaSpr, ObjectItemInfo item)
	{
		if (metaSpr.VfxSpritesheets.TryGetValue(item.TemplateID, out var sNode)) return sNode;
		var (_, slug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
		if (!string.IsNullOrEmpty(slug) && metaSpr.VfxSpritesheets.TryGetValue(slug, out sNode)) return sNode;
		if (!string.IsNullOrEmpty(item.ModelPath) && metaSpr.VfxSpritesheets.TryGetValue(item.ModelPath, out sNode)) return sNode;
		if (!string.IsNullOrEmpty(slug) && metaSpr.VfxSpritesheets.TryGetValue($"{slug}.rtex", out sNode)) return sNode;
		if (!string.IsNullOrEmpty(slug))
		{
			var matchKvp = metaSpr.VfxSpritesheets.FirstOrDefault(k => string.Equals(Path.GetFileNameWithoutExtension(k.Key), slug, StringComparison.OrdinalIgnoreCase));
			return matchKvp.Value;
		}

		return null;
	}

	private void UpdateSpritesheetMetadata(string wsPath, ObjectItemInfo item, string newId, string rtex, int cols, int rows, float fps, bool blend)
	{
		GameContext.MetadataService.UpdateMetadata(wsPath, m =>
		{
			m.VfxSpritesheets ??= new(StringComparer.OrdinalIgnoreCase);
			if (!string.Equals(item.TemplateID, newId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(item.TemplateID))
			{
				RemoveOldSpritesheetKeys(m, item);
			}

			m.VfxSpritesheets[newId] = new VfxMetadata
			{
				Columns = cols,
				Rows = rows,
				Fps = fps,
				SubframeBlend = blend,
				TexturePath = rtex
			};
		});
	}

	private void RemoveOldSpritesheetKeys(MapMetadata m, ObjectItemInfo item)
	{
		var (_, oldSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
		var keysToRemove = m.VfxSpritesheets.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, oldSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(oldSlug) && (string.Equals(k, $"{oldSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), oldSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("spritesheet", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();
		foreach (var k in keysToRemove) m.VfxSpritesheets.Remove(k);
	}

	private JsonObject TryGetDecalData(string wsPath, string templateID)
	{
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaDec) || metaDec?.Decals == null)
		{
			return new JsonObject();
		}

		if (metaDec.Decals.TryGetValue(templateID, out var dNode) && dNode != null)
		{
			return SerializeDecalNode(dNode);
		}

		var (_, slug) = TemplateIDHelper.ParseTemplateID(templateID);
		if (!string.IsNullOrEmpty(slug) && metaDec.Decals.TryGetValue(slug, out var sNode) && sNode != null)
		{
			return SerializeDecalNode(sNode);
		}

		return new JsonObject();
	}

	private JsonObject SerializeDecalNode(object dNode)
	{
		var node = JsonSerializer.SerializeToNode(dNode);
		if (node is JsonObject jObj) return jObj;
		return new JsonObject();
	}

	private string GetDecalIdFromUpdatedObj(JsonObject updatedObj, string fallbackId)
	{
		if (updatedObj.TryGetPropertyValue("decal_id", out var idNode) && !string.IsNullOrWhiteSpace(idNode?.ToString()))
		{
			return idNode.ToString().Trim();
		}

		if (updatedObj.TryGetPropertyValue("DecalId", out var idNode2) && !string.IsNullOrWhiteSpace(idNode2?.ToString()))
		{
			return idNode2.ToString().Trim();
		}

		return fallbackId;
	}

	private void OpenDecalEditDialog(ObjectItemInfo item, string wsPath)
	{
		JsonObject curDecData = TryGetDecalData(wsPath, item.TemplateID);
		_decalEditDialog.OpenForDecal(item.TemplateID, curDecData, updatedObj =>
		{
			string newDecalId = GetDecalIdFromUpdatedObj(updatedObj, item.TemplateID);

			var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
			var deserializedDecal = JsonSerializer.Deserialize<DecalMetadata>(updatedObj.ToJsonString(), serializerOptions) ?? new DecalMetadata();

			GameContext.MetadataService.UpdateMetadata(wsPath, m =>
			{
				m.Decals ??= new(StringComparer.OrdinalIgnoreCase);
				bool idChanged = !string.Equals(item.TemplateID, newDecalId, StringComparison.OrdinalIgnoreCase);

				if (idChanged)
				{
					m.Decals.Remove(item.TemplateID);
					var (_, oldSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
					if (!string.IsNullOrEmpty(oldSlug))
					{
						m.Decals.Remove(oldSlug);
					}
				}

				m.Decals[newDecalId] = deserializedDecal;
			});
			_currentPreviewTemplateID = newDecalId;
			RefreshObjectList();
		});
		_decalEditDialog.SetRandomizeAssetsAction(() =>
		{
			_decalEditDialog.RandomizeAssets();
			RefreshObjectList();
		});
	}

	private void OpenShaderEditDialog(ObjectItemInfo item, string wsPath)
	{
		_shaderEditDialog.SetRandomizeAssetsAction(() =>
		{
			GameContext.EditorService.AssignRandomAssetsToTemplate(item.TemplateID, overwriteExisting: true);
			RefreshObjectList();
			OpenEditDialogForObject(item);
		});
		_shaderEditDialog.OpenForShader(item.TemplateID, updatedConfig =>
		{
			GameContext.MetadataService.UpdateMetadata(wsPath, m =>
			{
				m.SpawnShaders ??= new(StringComparer.OrdinalIgnoreCase);
				if (!string.Equals(item.TemplateID, updatedConfig.Key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(item.TemplateID))
				{
					m.SpawnShaders.Remove(item.TemplateID);
					var (_, oldSlug) = TemplateIDHelper.ParseTemplateID(item.TemplateID);
					if (!string.IsNullOrEmpty(oldSlug))
					{
						m.SpawnShaders.Remove(oldSlug);
					}
				}

				m.SpawnShaders[updatedConfig.Key] = new SpawnShaderMetadata
				{
					Name = updatedConfig.Name,
					TransitionMode = updatedConfig.TransitionMode,
					Direction = updatedConfig.Direction,
					EdgeColor = "#" + updatedConfig.EdgeColor.ToHtml(true),
					EdgeWidth = updatedConfig.EdgeWidth,
					EdgeEmission = updatedConfig.EdgeEmission,
					NoiseScale = updatedConfig.NoiseScale,
					NoiseRoughness = updatedConfig.NoiseRoughness,
					FresnelPower = updatedConfig.FresnelPower,
					VertexDisplacement = updatedConfig.VertexDisplacement,
					AlphaFade = updatedConfig.AlphaFade,
					Duration = updatedConfig.Duration,
					AssetType = !string.IsNullOrWhiteSpace(updatedConfig.AssetType) ? updatedConfig.AssetType : "SpawnShader"
				};
			});
			_currentPreviewTemplateID = updatedConfig.Key;
			RefreshObjectList();
		});
	}

	private void OpenVfxEditDialog(ObjectItemInfo item, string wsPath)
	{
		if (GameContext.MetadataService.TryLoadMetadata(wsPath, out var metaVfx) && metaVfx?.Templates?.Vfx != null)
		{
			var vfxConfig = metaVfx.Templates.Vfx.FirstOrDefault(x => string.Equals(x.VfxId, item.TemplateID, StringComparison.OrdinalIgnoreCase));
			if (vfxConfig != null)
			{
				_vfxStudioDialog.SetRandomizeAssetsAction(() =>
				{
					GameContext.EditorService.AssignRandomAssetsToTemplate(item.TemplateID, overwriteExisting: true);
					RefreshObjectList();
					OpenEditDialogForObject(item);
				});
				_vfxStudioDialog.OpenForConfig(vfxConfig, updatedCfg =>
				{
					GameContext.MetadataService.UpdateMetadata(wsPath, m =>
					{
						if (!string.Equals(item.TemplateID, updatedCfg.VfxId, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(item.TemplateID))
						{
							m.RemoveVfx(item.TemplateID);
						}

						m.AddOrUpdateVfx(updatedCfg);
					});
					if (!string.IsNullOrEmpty(_pendingNewVfxTemplateId) &&
					    (string.Equals(item.TemplateID, _pendingNewVfxTemplateId, StringComparison.OrdinalIgnoreCase) ||
					     string.Equals(updatedCfg.VfxId, _pendingNewVfxTemplateId, StringComparison.OrdinalIgnoreCase)))
					{
						_pendingNewVfxWasApplied = true;
					}

					_currentPreviewTemplateID = updatedCfg.VfxId;
					RefreshObjectList();
				});
			}
		}
	}

	private void HandleVfxStudioDialogClosed()
	{
		if (string.IsNullOrEmpty(_pendingNewVfxTemplateId)) return;

		string pendingTemplateId = _pendingNewVfxTemplateId;
		bool wasApplied = _pendingNewVfxWasApplied;
		_pendingNewVfxTemplateId = "";
		_pendingNewVfxWasApplied = false;

		if (wasApplied) return;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		GameContext.RegistryService.UnregisterVfxConfig(pendingTemplateId);
		GameContext.MetadataService.UpdateMetadata(wsPath, meta => meta.RemoveVfx(pendingTemplateId));
		RefreshObjectList();
	}

	private void OpenDefaultEditDialog(ObjectItemInfo item)
	{
		if (item.Category == "upgrades" || item.Category == "items")
		{
			_itemUpgradeEditDialog.OpenForObject(item.Category, item.TemplateID, (oldId, newId) =>
			{
				_currentPreviewTemplateID = newId;
				RefreshObjectList();
			});
			_itemUpgradeEditDialog.SetRandomizeAssetsAction(() =>
			{
				_itemUpgradeEditDialog.RandomizeAssets();
				RefreshObjectList();
			});
		}
		else
		{
			_entityVisualEditDialog.SetRandomizeAssetsAction(() =>
			{
				GameContext.EditorService.AssignRandomAssetsToTemplate(item.TemplateID, overwriteExisting: true);
				_entityVisualEditDialog.RandomizeAssets();
				RefreshObjectList();
			});
			_entityVisualEditDialog.OpenForObject(item.Category, item.TemplateID, (oldId, newId) =>
			{
				_currentPreviewTemplateID = newId;
				RefreshObjectList();
			});
		}
	}
}
