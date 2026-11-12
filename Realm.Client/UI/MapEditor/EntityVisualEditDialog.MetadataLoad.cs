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
	private void InitializeDialogState(string? category, string objectId, Node? selectedObject, Action<string, string>? onApplied)
	{
		_currentSelectedObject = selectedObject;
		_modelLocalMinY = CalculateInitialModelLocalMinY(selectedObject);
		_category = (category ?? "units").ToLowerInvariant();
		_objectType = DetermineObjectType(_category);
		_originalTemplateID = objectId ?? "";

		var (parsedType, parsedSlug) = TemplateIDHelper.ParseTemplateID(_originalTemplateID);
		_slug = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug : TemplateIDHelper.GenerateSlug(_originalTemplateID);
		_onAppliedCallback = onApplied;
	}

	private float CalculateInitialModelLocalMinY(Node? selectedObject)
	{
		if (selectedObject != null) return CalculateModelLocalMinY(selectedObject);
		var editorObj = GameContext.Host?.SelectedEditorObject;
		return editorObj != null ? CalculateModelLocalMinY(editorObj) : 0f;
	}

	private string DetermineObjectType(string category)
	{
		return category switch
		{
			"units" => "unit",
			"buildings" => "building",
			"resources" => "resource",
			"props" => "prop",
			"items" => "item",
			_ => "unit"
		};
	}

	private void LoadMetadataByCategory(MapMetadata meta, string objectId)
	{
		switch (_category)
		{
			case "units":
				var u = meta.GetUnit(objectId);
				if (u != null) LoadFromUnitMetadata(u);
				break;
			case "buildings":
				var b = meta.GetBuilding(objectId);
				if (b != null) LoadFromUnitMetadata(b);
				break;
			case "resources":
				var r = meta.GetResource(objectId);
				if (r != null) LoadFromResourceMetadata(r);
				break;
			case "props":
				var p = meta.GetProp(objectId);
				if (p != null) LoadFromPropMetadata(p);
				break;
			case "items":
				var i = meta.GetItem(objectId);
				if (i != null) LoadFromItemMetadata(i);
				break;
		}
	}

	private void InitializeSnapshot()
	{
		_previousScale = _scale;

		_initialSnapshot = new VisualOverridesSnapshot
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
			SpawnShader = _spawnShader,
			DeathShader = _deathShader,
			EnableProceduralAnimation = _enableProceduralAnimation,
			ProceduralAnimation = _proceduralAnimation
		};

		string targetId = !string.IsNullOrEmpty(_originalTemplateID) ? _originalTemplateID : $"{_objectType}/{_slug}";
		_initialTemplateJson = GameContext.EditorService.SnapshotTemplateJson(targetId);
		_templateExistedInitially = _initialTemplateJson != null;
	}

	private void InitializeModelProperties(object targetLookup, string parsedSlug)
	{
		var host = GameContext.Host;
		if (host != null)
		{
			LoadPropertiesFromHost(host, targetLookup);
		}
		else
		{
			SetDefaultModelProperties();
		}

		ValidateModelPath();

		_portraitModelPath = "";
		_visualMode = DetermineVisualMode();
		_name = !string.IsNullOrEmpty(parsedSlug) ? parsedSlug.Replace("_", " ") : _originalTemplateID;
		_description = "";
	}

	private void LoadPropertiesFromHost(GameHost host, object targetLookup)
	{
		_scale = host.GetModelScale(targetLookup);
		_yOffset = host.GetModelYOffset(targetLookup);
		_collisionCircle = host.GetModelCollisionCircleRatio(targetLookup);
		_brightness = host.GetModelBrightness(targetLookup);
		_tint = host.GetModelColorTint(targetLookup);
		_normalizeLuminance = host.GetModelNormalizeLuminance(targetLookup);
		_ignorePlayerColor = host.GetModelIgnorePlayerColor(targetLookup);
		_despillPlayerColor = host.GetModelDespillPlayerColor(targetLookup);
		_spawnShader = host.GetModelSpawnShader(targetLookup) ?? "";
		_deathShader = host.GetModelDeathShader(targetLookup) ?? "";
		_enableProceduralAnimation = host.GetModelEnableProceduralAnimation(targetLookup);
		_proceduralAnimation = host.GetModelProceduralAnimation(targetLookup) ?? "";
		_modelPath = host.GetModelAssetKey(targetLookup) ?? "";
	}

	private void SetDefaultModelProperties()
	{
		_scale = 1.0f;
		_yOffset = 0.0f;
		_collisionCircle = 1.0f;
		_brightness = 0.5f;
		_tint = Colors.White;
		_normalizeLuminance = true;
		_ignorePlayerColor = false;
		_despillPlayerColor = false;
		_spawnShader = "";
		_deathShader = "";
		_enableProceduralAnimation = false;
		_proceduralAnimation = "";
		_modelPath = "";
	}

	private void ValidateModelPath()
	{
		if (!string.IsNullOrEmpty(_modelPath) &&
		    !_modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) &&
		    !_modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) &&
		    !_modelPath.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase))
		{
			_modelPath = "";
		}
	}

	private string DetermineVisualMode()
	{
		return (_modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) ||
		        _modelPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
		        _modelPath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
			? "GroundPlane"
			: "Mesh";
	}

	private void LoadFromUnitMetadata(UnitMetadata u)
	{
		ApplyBaseMetadataProperties(u.TemplateID, u.ModelPath, u.PortraitModelPath, u.VisualMode, u.Name, u.Description, u.Scale, u.YOffset, u.CollisionCircle, u.Brightness, u.Tint, u.NormalizeLuminance, u.IgnorePlayerColor, u.DespillPlayerColor, u.SpawnShader, u.DeathShader);
	}

	private void LoadFromResourceMetadata(ResourceMetadata r)
	{
		ApplyBaseMetadataProperties(r.TemplateID, r.ModelPath, r.PortraitModelPath, r.VisualMode, r.Name, r.Description, r.Scale, r.YOffset, r.CollisionCircle, r.Brightness, r.Tint, r.NormalizeLuminance, r.IgnorePlayerColor, r.DespillPlayerColor, r.SpawnShader, r.DeathShader);
	}

	private void LoadFromPropMetadata(PropMetadata p)
	{
		ApplyBaseMetadataProperties(p.TemplateID, p.ModelPath, p.PortraitModelPath, p.VisualMode, p.Name, p.Description, p.Scale, p.YOffset, p.CollisionCircle, p.Brightness, p.Tint, p.NormalizeLuminance, p.IgnorePlayerColor, p.DespillPlayerColor, p.SpawnShader, p.DeathShader);
	}

	private void ApplyBaseMetadataProperties(string templateId, string? modelPath, string? portraitModelPath, string? visualMode, string? name, string? description, float scale, float yOffset, float collisionCircle, float brightness, string? tint, bool normalizeLuminance, bool ignorePlayerColor, bool despillPlayerColor, string? spawnShader, string? deathShader)
	{
		ApplyModelPaths(modelPath, portraitModelPath);
		ApplyBasicProperties(visualMode, name, description, scale, yOffset, collisionCircle, brightness);
		ApplyColorProperties(tint, normalizeLuminance, ignorePlayerColor, despillPlayerColor);
		ApplyShaderProperties(spawnShader, deathShader);
		ApplyProceduralAnimationProperties(templateId, modelPath);
	}

	private void ApplyModelPaths(string? modelPath, string? portraitModelPath)
	{
		if (!string.IsNullOrEmpty(modelPath) && (modelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || modelPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || modelPath.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)))
		{
			_modelPath = modelPath;
		}

		if (!string.IsNullOrEmpty(portraitModelPath) && portraitModelPath.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			_portraitModelPath = portraitModelPath;
		}
	}

	private void ApplyBasicProperties(string? visualMode, string? name, string? description, float scale, float yOffset, float collisionCircle, float brightness)
	{
		_visualMode = !string.IsNullOrEmpty(visualMode) ? visualMode : _visualMode;
		_name = name ?? _name;
		_description = description ?? _description;
		_scale = scale > 0 ? scale : _scale;
		_yOffset = yOffset;
		_collisionCircle = collisionCircle > 0 ? collisionCircle : _collisionCircle;
		_brightness = brightness;
	}

	private void ApplyColorProperties(string? tint, bool normalizeLuminance, bool ignorePlayerColor, bool despillPlayerColor)
	{
		_tint = !string.IsNullOrEmpty(tint) && tint.StartsWith("#") ? Color.FromHtml(tint) : _tint;
		_normalizeLuminance = normalizeLuminance;
		_ignorePlayerColor = ignorePlayerColor;
		_despillPlayerColor = despillPlayerColor;
	}

	private void ApplyShaderProperties(string? spawnShader, string? deathShader)
	{
		if (!string.IsNullOrEmpty(spawnShader)) _spawnShader = spawnShader;
		if (!string.IsNullOrEmpty(deathShader)) _deathShader = deathShader;
	}

	private void ApplyProceduralAnimationProperties(string templateId, string? modelPath)
	{
		string normKey = NormalizeModelAssetKey(templateId);
		string normModel = !string.IsNullOrEmpty(modelPath) ? NormalizeModelAssetKey(modelPath) : "";
		if (GameContext.Host != null && (!string.IsNullOrEmpty(normKey) || !string.IsNullOrEmpty(normModel)))
		{
			string procAnim = GameContext.Host.GetModelProceduralAnimation(templateId);
			if (!string.IsNullOrEmpty(procAnim)) _proceduralAnimation = procAnim;
			_enableProceduralAnimation = GameContext.Host.GetModelEnableProceduralAnimation(templateId);
		}
	}

	private static string NormalizeModelAssetKey(string key)
	{
		if (string.IsNullOrEmpty(key)) return "";
		string trimmed = key.Trim();
		if (trimmed.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
			trimmed = trimmed.Substring(6);
		return trimmed.Replace("\\", "/").TrimStart('/');
	}
}
