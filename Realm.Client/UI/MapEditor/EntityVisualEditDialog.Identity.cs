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
	private static (string Category, string ObjectId) ResolveObjectCategoryAndId(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject))
			return ("units", string.Empty);

		string objectId = GameContext.Host?.GetSelectedEntityOrAssetKey(selectedObject) ?? string.Empty;

		if (selectedObject is Unit3D unit)
		{
			return ResolveUnitCategoryAndId(unit, objectId);
		}

		if (selectedObject is Prop3D prop)
		{
			return ResolvePropCategoryAndId(prop, objectId);
		}

		if (selectedObject is LootItem3D lootItem && !string.IsNullOrEmpty(lootItem.ItemTypeId))
		{
			return (ItemCategoryName, lootItem.ItemTypeId);
		}

		return ResolvePrefixOrRegistryCategoryAndId(objectId);
	}

	private static (string Category, string ObjectId) ResolveUnitCategoryAndId(Unit3D unit, string objectId)
	{
		string cat = unit.IsBuilding ? "buildings" : "units";
		string id = !string.IsNullOrEmpty(unit.UnitId) ? unit.UnitId : objectId;
		return (cat, id);
	}

	private static (string Category, string ObjectId) ResolvePropCategoryAndId(Prop3D prop, string objectId)
	{
		string id = !string.IsNullOrEmpty(prop.PropId) ? prop.PropId : objectId;
		string cat = (id.StartsWith("resource/", StringComparison.OrdinalIgnoreCase) ||
		              (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.ContainsKey(id)))
			? "resources"
			: "props";
		return (cat, id);
	}

	private static (string Category, string ObjectId) ResolvePrefixOrRegistryCategoryAndId(string objectId)
	{
		if (!string.IsNullOrEmpty(objectId))
		{
			if (TryGetCategoryFromPrefix(objectId, out string? cat)) return (cat, objectId);
			if (TryGetCategoryFromRegistry(objectId, out string? regCat)) return (regCat, objectId);
		}

		return ("props", objectId);
	}

	private static bool TryGetCategoryFromPrefix(string objectId, [NotNullWhen(true)] out string? category)
	{
		if (objectId.StartsWith("unit/", StringComparison.OrdinalIgnoreCase))
		{
			category = "units";
			return true;
		}

		if (objectId.StartsWith("building/", StringComparison.OrdinalIgnoreCase))
		{
			category = "buildings";
			return true;
		}

		if (objectId.StartsWith("resource/", StringComparison.OrdinalIgnoreCase))
		{
			category = "resources";
			return true;
		}

		if (objectId.StartsWith("prop/", StringComparison.OrdinalIgnoreCase))
		{
			category = "props";
			return true;
		}

		category = null;
		return false;
	}

	private static bool TryGetCategoryFromRegistry(string objectId, [NotNullWhen(true)] out string? category)
	{
		if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(objectId))
		{
			category = "buildings";
			return true;
		}

		if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.ContainsKey(objectId))
		{
			category = "units";
			return true;
		}

		if (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.ContainsKey(objectId))
		{
			category = "resources";
			return true;
		}

		if (GameHost.PropRegistry != null && GameHost.PropRegistry.ContainsKey(objectId))
		{
			category = "props";
			return true;
		}

		category = null;
		return false;
	}

	public static void UpdateStaticRegistryModelAndVisualMode(string category, string key, string modelPath, string visualMode)
	{
		if (string.IsNullOrEmpty(key)) return;
		StringName sn = (StringName)key;

		switch (category)
		{
			case "units":
				if (GameHost.UnitRegistry.TryGetValue(sn, out var u))
				{
					u.ModelPath = modelPath;
					u.VisualMode = visualMode;
					GameHost.UnitRegistry[sn] = u;
				}
				else
				{
					GameHost.UnitRegistry[sn] = new UnitMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}

				break;

			case "buildings":
				if (GameHost.BuildingRegistry.TryGetValue(sn, out var b))
				{
					b.ModelPath = modelPath;
					b.VisualMode = visualMode;
					GameHost.BuildingRegistry[sn] = b;
				}
				else
				{
					GameHost.BuildingRegistry[sn] = new UnitMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}

				break;

			case "resources":
				if (GameHost.ResourceRegistry.TryGetValue(sn, out var r))
				{
					r.ModelPath = modelPath;
					r.VisualMode = visualMode;
					GameHost.ResourceRegistry[sn] = r;
				}
				else
				{
					GameHost.ResourceRegistry[sn] = new ResourceMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}

				break;

			case "items":
				if (GameContext.RegistryService != null && GameContext.RegistryService.ItemRegistry.TryGetValue(key, out var itemTemplate))
				{
					itemTemplate.ModelPath = modelPath;
				}

				break;

			case "props":
				if (GameHost.PropRegistry.TryGetValue(sn, out var p))
				{
					p.ModelPath = modelPath;
					p.VisualMode = visualMode;
					GameHost.PropRegistry[sn] = p;
				}
				else
				{
					GameHost.PropRegistry[sn] = new PropMetadata
					{
						TemplateID = key,
						ModelPath = modelPath,
						VisualMode = visualMode
					};
				}

				break;
		}
	}


	private void ApplyLiveModelAndVisualMode()
	{
		if (GameContext.Host == null || string.IsNullOrEmpty(_originalTemplateID)) return;

		ApplyToMultipleRegistries();

		Prop3D.InvalidateModelPathCache(_originalTemplateID);
		ModelCache.InvalidateModelPath(_originalTemplateID);
		if (!string.IsNullOrEmpty(_modelPath))
		{
			Prop3D.InvalidateModelPathCache(_modelPath);
			ModelCache.InvalidateModelPath(_modelPath);
		}

		GameContext.IEditorAPI.RefreshPlacedObjectModels(_originalTemplateID);

		if (!string.IsNullOrEmpty(_slug))
		{
			GameContext.IEditorAPI.RefreshPlacedObjectModels($"{_objectType}/{_slug}");
		}

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
			else if (_currentSelectedObject is LootItem3D lootItem)
			{
				lootItem.RebuildVisuals();
			}

			_modelLocalMinY = CalculateModelLocalMinY(_currentSelectedObject);
		}

		UpdateStudioButtonVisibility();
	}

	private void ApplyToMultipleRegistries()
	{
		UpdateStaticRegistryModelAndVisualMode(_category, _originalTemplateID, _modelPath, _visualMode);

		string cleanId = Path.GetFileNameWithoutExtension(_originalTemplateID);
		if (!string.IsNullOrEmpty(cleanId) && !cleanId.Equals(_originalTemplateID, StringComparison.OrdinalIgnoreCase))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, cleanId, _modelPath, _visualMode);
		}

		if (!string.IsNullOrEmpty(_slug))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, $"{_objectType}/{_slug}", _modelPath, _visualMode);
			UpdateStaticRegistryModelAndVisualMode(_category, _slug, _modelPath, _visualMode);
		}

		if (_currentSelectedObject is Unit3D unit && !string.IsNullOrEmpty(unit.UnitId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, unit.UnitId, _modelPath, _visualMode);
		}
		else if (_currentSelectedObject is Prop3D prop && !string.IsNullOrEmpty(prop.PropId))
		{
			UpdateStaticRegistryModelAndVisualMode(_category, prop.PropId, _modelPath, _visualMode);
		}
	}
}
