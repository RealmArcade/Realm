using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public partial class EntityVisualEditDialog
{
	protected override void OnApply()
	{
		if (_txtSlug == null || string.IsNullOrWhiteSpace(_txtSlug.Text))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Object slug cannot be empty."));
			return;
		}

		string animKey = GetSelectedProceduralAnimationKey();
		string spawnKey = GetSelectedSpawnShaderKey();
		string deathKey = GetSelectedDeathShaderKey();

		string newTemplateID = $"{_objectType}/{_slug}";
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, newTemplateID, _modelPath, _visualMode);
		}

		var currentSnapshot = CreateVisualOverridesSnapshot(spawnKey, deathKey, animKey);

		UpdateMetadata(wsPath, newTemplateID, spawnKey, deathKey);

		if (GameContext.Host != null)
		{
			FlushAndRefreshGameHost(newTemplateID, spawnKey, deathKey, animKey, wsPath, currentSnapshot);
		}

		Hud?.RefreshEntityPalette();
		_onAppliedCallback?.Invoke(_originalTemplateID, newTemplateID);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Saved visual properties for '{0}'"), newTemplateID));
	}

	private string GetSelectedProceduralAnimationKey()
	{
		if (_optProceduralAnim != null && _optProceduralAnim.Selected > 0)
		{
			var animConfigs = ProceduralAnimationManager.LoadAllConfigs();
			if (_optProceduralAnim.Selected - 1 < animConfigs.Count)
			{
				return animConfigs.ElementAt(_optProceduralAnim.Selected - 1).Key;
			}
		}

		return "";
	}

	private string GetSelectedSpawnShaderKey()
	{
		if (_optSpawnShader != null && _optSpawnShader.Selected > 0)
		{
			var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
			if (_optSpawnShader.Selected - 1 < currentShaders.Count)
			{
				return currentShaders.ElementAt(_optSpawnShader.Selected - 1).Key;
			}
		}

		return "";
	}

	private string GetSelectedDeathShaderKey()
	{
		if (_optDeathShader != null && _optDeathShader.Selected > 0)
		{
			var currentShaders = SpawnDeathShaderManager.LoadAllCustomShaders();
			if (_optDeathShader.Selected - 1 < currentShaders.Count)
			{
				return currentShaders.ElementAt(_optDeathShader.Selected - 1).Key;
			}
		}

		return "";
	}

	private VisualOverridesSnapshot CreateVisualOverridesSnapshot(string spawnKey, string deathKey, string animKey)
	{
		return new VisualOverridesSnapshot
		{
			ModelPath = _modelPath,
			VisualMode = _visualMode,
			Scale = _scale,
			YOffset = _yOffset,
			CollisionCircleRatio = _collisionCircle,
			Brightness = _brightness,
			ColorTint = _tint,
			NormalizeLuminance = _normalizeLuminance,
			IgnorePlayerColor = _ignorePlayerColor,
			DespillPlayerColor = _despillPlayerColor,
			SpawnShader = spawnKey,
			DeathShader = deathKey,
			EnableProceduralAnimation = _enableProceduralAnimation,
			ProceduralAnimation = animKey
		};
	}

	private void UpdateMetadata(string wsPath, string newTemplateID, string spawnKey, string deathKey)
	{
		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			string tintHex = $"#{_tint.ToHtml(false)}";

			switch (_category)
			{
				case "units":
					ApplyUnitMetadata(meta, newTemplateID, tintHex, spawnKey, deathKey);
					break;
				case "buildings":
					ApplyBuildingMetadata(meta, newTemplateID, tintHex, spawnKey, deathKey);
					break;
				case "resources":
					ApplyResourceMetadata(meta, newTemplateID, tintHex, spawnKey, deathKey);
					break;
				case "props":
					ApplyPropMetadata(meta, newTemplateID, tintHex, spawnKey, deathKey);
					break;
			}
		});
	}

	private void FlushAndRefreshGameHost(string newTemplateID, string spawnKey, string deathKey, string animKey, string wsPath, VisualOverridesSnapshot currentSnapshot)
	{
		bool templateRenamed = !string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID);

		FlushModelPropertiesToGameHost(newTemplateID, spawnKey, deathKey, animKey);

		if (templateRenamed)
		{
			FlushModelPropertiesToGameHost(_originalTemplateID, spawnKey, deathKey, animKey);
			GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
		}

		if (GameContext.Host is { } host) host.LoadUnitMetadata(wsPath);

		InvalidateModelCaches(newTemplateID);

		var action = new EntityVisualEditUndoAction(newTemplateID, _category, _initialSnapshot, currentSnapshot);
		EditorHistoryManager.RecordAction(action);

		GameContext.IEditorAPI.RefreshPlacedObjectModels(newTemplateID);
		if (templateRenamed)
		{
			GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);
		}

		RefreshCurrentSelectedObject();

		if (GameContext.Host is { } flushHost)
		{
			flushHost.FlushModelYOffsetSave();
			flushHost.FlushModelCollisionCircleSave();
		}
	}

	private void InvalidateModelCaches(string newTemplateID)
	{
		Prop3D.InvalidateModelPathCache(newTemplateID);
		ModelCache.InvalidateModelPath(newTemplateID);
		Prop3D.InvalidateModelPathCache(_originalTemplateID);
		ModelCache.InvalidateModelPath(_originalTemplateID);
		if (!string.IsNullOrEmpty(_modelPath))
		{
			Prop3D.InvalidateModelPathCache(_modelPath);
			ModelCache.InvalidateModelPath(_modelPath);
		}
	}

	private void RefreshCurrentSelectedObject()
	{
		if (_currentSelectedObject != null && GodotObject.IsInstanceValid(_currentSelectedObject))
		{
			if (_currentSelectedObject is Unit3D u3d && GameContext.Host is { } host)
			{
				string fallbackModel = host.GetFallbackModelPath(_modelPath, u3d.IsBuilding);
				u3d.LoadModel(fallbackModel);
			}
			else if (_currentSelectedObject is Prop3D p3d)
			{
				p3d.RefreshPropVisual();
			}
		}
	}

	private void ApplyUnitMetadata(MapMetadata meta, string newTemplateID, string tintHex, string spawnKey, string deathKey)
	{
		bool updatedU = meta.UpdateUnit(_originalTemplateID, u =>
		{
			u.TemplateID = newTemplateID;
			u.Name = _name;
			u.Description = _description;
			u.ModelPath = _modelPath;
			u.PortraitModelPath = _portraitModelPath;
			u.VisualMode = _visualMode;
			u.Scale = _scale;
			u.YOffset = _yOffset;
			u.CollisionCircle = _collisionCircle;
			u.Brightness = _brightness;
			u.Tint = tintHex;
			u.NormalizeLuminance = _normalizeLuminance;
			u.IgnorePlayerColor = _ignorePlayerColor;
			u.DespillPlayerColor = _despillPlayerColor;
			u.SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey;
			u.DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey;
			return u;
		});
		if (!updatedU)
		{
			meta.AddOrUpdateUnit(new UnitMetadata
			{
				TemplateID = newTemplateID,
				Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
				Description = _description,
				ModelPath = _modelPath,
				PortraitModelPath = _portraitModelPath,
				VisualMode = _visualMode,
				Scale = _scale,
				YOffset = _yOffset,
				CollisionCircle = _collisionCircle,
				Brightness = _brightness,
				Tint = tintHex,
				NormalizeLuminance = _normalizeLuminance,
				IgnorePlayerColor = _ignorePlayerColor,
				DespillPlayerColor = _despillPlayerColor,
				PathingType = 8,
				MaxHp = 100,
				SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey,
				DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey
			});
		}
	}

	private void ApplyBuildingMetadata(MapMetadata meta, string newTemplateID, string tintHex, string spawnKey, string deathKey)
	{
		bool updatedB = meta.UpdateBuilding(_originalTemplateID, b =>
		{
			b.TemplateID = newTemplateID;
			b.Name = _name;
			b.Description = _description;
			b.ModelPath = _modelPath;
			b.PortraitModelPath = _portraitModelPath;
			b.VisualMode = _visualMode;
			b.Scale = _scale;
			b.YOffset = _yOffset;
			b.CollisionCircle = _collisionCircle;
			b.Brightness = _brightness;
			b.Tint = tintHex;
			b.NormalizeLuminance = _normalizeLuminance;
			b.IgnorePlayerColor = _ignorePlayerColor;
			b.DespillPlayerColor = _despillPlayerColor;
			b.SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey;
			b.DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey;
			return b;
		});
		if (!updatedB)
		{
			meta.AddOrUpdateBuilding(new UnitMetadata
			{
				TemplateID = newTemplateID,
				Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
				Description = _description,
				ModelPath = _modelPath,
				PortraitModelPath = _portraitModelPath,
				VisualMode = _visualMode,
				Scale = _scale,
				YOffset = _yOffset,
				CollisionCircle = _collisionCircle,
				Brightness = _brightness,
				Tint = tintHex,
				NormalizeLuminance = _normalizeLuminance,
				IgnorePlayerColor = _ignorePlayerColor,
				DespillPlayerColor = _despillPlayerColor,
				PathingType = 32,
				MaxHp = 1000,
				SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey,
				DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey
			});
		}
	}

	private void ApplyResourceMetadata(MapMetadata meta, string newTemplateID, string tintHex, string spawnKey, string deathKey)
	{
		bool updatedR = meta.UpdateResource(_originalTemplateID, r =>
		{
			r.TemplateID = newTemplateID;
			r.Name = _name;
			r.Description = _description;
			r.ModelPath = _modelPath;
			r.PortraitModelPath = _portraitModelPath;
			r.VisualMode = _visualMode;
			r.Scale = _scale;
			r.YOffset = _yOffset;
			r.CollisionCircle = _collisionCircle;
			r.Brightness = _brightness;
			r.Tint = tintHex;
			r.NormalizeLuminance = _normalizeLuminance;
			r.IgnorePlayerColor = _ignorePlayerColor;
			r.DespillPlayerColor = _despillPlayerColor;
			r.SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey;
			r.DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey;
			return r;
		});
		if (!updatedR)
		{
			meta.AddOrUpdateResource(new ResourceMetadata
			{
				TemplateID = newTemplateID,
				Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
				Description = _description,
				ModelPath = _modelPath,
				PortraitModelPath = _portraitModelPath,
				VisualMode = _visualMode,
				Scale = _scale,
				YOffset = _yOffset,
				CollisionCircle = _collisionCircle,
				Brightness = _brightness,
				Tint = tintHex,
				NormalizeLuminance = _normalizeLuminance,
				IgnorePlayerColor = _ignorePlayerColor,
				DespillPlayerColor = _despillPlayerColor,
				PathingType = 255,
				MaxCapacity = 2000,
				HarvestRate = 10,
				MaxWorkers = 5,
				SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey,
				DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey
			});
		}
	}

	private void ApplyPropMetadata(MapMetadata meta, string newTemplateID, string tintHex, string spawnKey, string deathKey)
	{
		bool updatedP = meta.UpdateProp(_originalTemplateID, p =>
		{
			p.TemplateID = newTemplateID;
			p.Name = _name;
			p.Description = _description;
			p.ModelPath = _modelPath;
			p.PortraitModelPath = _portraitModelPath;
			p.VisualMode = _visualMode;
			p.Scale = _scale;
			p.YOffset = _yOffset;
			p.CollisionCircle = _collisionCircle;
			p.Brightness = _brightness;
			p.Tint = tintHex;
			p.NormalizeLuminance = _normalizeLuminance;
			p.IgnorePlayerColor = _ignorePlayerColor;
			p.DespillPlayerColor = _despillPlayerColor;
			p.SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey;
			p.DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey;
			return p;
		});
		if (!updatedP)
		{
			meta.AddOrUpdateProp(new PropMetadata
			{
				TemplateID = newTemplateID,
				Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
				Description = _description,
				ModelPath = _modelPath,
				PortraitModelPath = _portraitModelPath,
				VisualMode = _visualMode,
				Scale = _scale,
				YOffset = _yOffset,
				CollisionCircle = _collisionCircle,
				Brightness = _brightness,
				Tint = tintHex,
				NormalizeLuminance = _normalizeLuminance,
				IgnorePlayerColor = _ignorePlayerColor,
				DespillPlayerColor = _despillPlayerColor,
				PathingType = 255,
				SpawnShader = string.IsNullOrWhiteSpace(spawnKey) ? null : spawnKey,
				DeathShader = string.IsNullOrWhiteSpace(deathKey) ? null : deathKey
			});
		}
	}

	private void FlushModelPropertiesToGameHost(string templateID, string spawnKey, string deathKey, string animKey)
	{
		GameContext.IEditorAPI.SetModelScale(templateID, _scale);
		GameContext.IEditorAPI.SetModelYOffset(templateID, _yOffset);
		GameContext.IEditorAPI.SetModelCollisionCircleRatio(templateID, _collisionCircle);
		GameContext.IEditorAPI.SetModelBrightness(templateID, _brightness);
		GameContext.IEditorAPI.SetModelColorTint(templateID, _tint.R, _tint.G, _tint.B, _tint.A);
		GameContext.IEditorAPI.SetModelNormalizeLuminance(templateID, _normalizeLuminance);
		GameContext.IEditorAPI.SetModelIgnorePlayerColor(templateID, _ignorePlayerColor);
		GameContext.IEditorAPI.SetModelDespillPlayerColor(templateID, _despillPlayerColor);
		GameContext.IEditorAPI.SetModelSpawnShader(templateID, spawnKey);
		GameContext.IEditorAPI.SetModelDeathShader(templateID, deathKey);
		GameContext.IEditorAPI.SetModelEnableProceduralAnimation(templateID, _enableProceduralAnimation);
		GameContext.IEditorAPI.SetModelProceduralAnimation(templateID, animKey);
	}
}
