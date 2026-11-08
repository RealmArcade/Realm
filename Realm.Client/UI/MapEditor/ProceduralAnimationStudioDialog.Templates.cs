// The template catalogue behind the picker: presets, map templates, metadata templates, host
// registry entries and sample templates are all flattened into one id set before the picker is
// populated, so a template that exists in two sources still appears once.

using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Animation;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Terrain;


namespace Realm.Client.UI.MapEditor;

public partial class ProceduralAnimationStudioDialog
{
	private void SelectInitialTemplate()
	{
		if (_optTemplatePicker != null && _optTemplatePicker.Selected >= 0 && _optTemplatePicker.Selected < _availableTemplates.Count)
		{
			LoadPreviewTemplate(_availableTemplates[_optTemplatePicker.Selected]);
			return;
		}

		if (_availableTemplates.Count > 0)
		{
			LoadPreviewTemplate(_availableTemplates[0]);
		}
	}

	private void PopulateTemplateList()
	{
		_availableTemplates.Clear();
		if (_optTemplatePicker == null) return;
		_optTemplatePicker.Clear();

		var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();

		LoadMetadataTemplates(wsPath, existingIds);
		LoadGameHostRegistryTemplates(existingIds);
		LoadMapAssetsTemplates(wsPath, existingIds);
		AddSampleTemplates();

		PopulateTemplatePickerAndSelect();
	}

	private void AddTemplateIfNotExists(string id, string name, string category, string? modelPath, string? visualMode, HashSet<string> existingIds)
	{
		if (string.IsNullOrEmpty(id) || !existingIds.Add(id)) return;

		string actualVisualMode = !string.IsNullOrEmpty(visualMode) ? visualMode : (modelPath?.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) == true ? "GroundPlane" : "Mesh");
		_availableTemplates.Add(new TemplateItemInfo
		{
			Id = id,
			DisplayName = !string.IsNullOrEmpty(name) ? name : id,
			Category = category,
			ModelPath = modelPath ?? "",
			VisualMode = actualVisualMode
		});
	}

	private void LoadMetadataTemplates(string wsPath, HashSet<string> existingIds)
	{
		if (!GameContext.MetadataService.TryLoadMetadata(wsPath, out var meta) || meta?.Templates == null) return;

		AddMetadataTemplates(meta.Templates.Units, "Unit", existingIds, u => u.TemplateID, u => u.Name, u => u.ModelPath, u => u.VisualMode);
		AddMetadataTemplates(meta.Templates.Buildings, "Building", existingIds, b => b.TemplateID, b => b.Name, b => b.ModelPath, b => b.VisualMode);
		AddMetadataTemplates(meta.Templates.Resources, "Resource", existingIds, r => r.TemplateID, r => r.Name, r => r.ModelPath, r => r.VisualMode);
		AddMetadataTemplates(meta.Templates.Props, "Prop", existingIds, p => p.TemplateID, p => p.Name, p => p.ModelPath, p => p.VisualMode);
	}

	private void AddMetadataTemplates<T>(IEnumerable<T> items, string category, HashSet<string> existingIds, Func<T, string> getId, Func<T, string> getName, Func<T, string?> getModelPath, Func<T, string> getVisualMode)
	{
		if (items == null) return;

		foreach (var item in items)
			AddTemplateIfNotExists(getId(item), getName(item), category, getModelPath(item), getVisualMode(item), existingIds);
	}

	private void LoadGameHostRegistryTemplates(HashSet<string> existingIds)
	{
		if (GameHost.UnitRegistry != null)
			foreach (var kvp in GameHost.UnitRegistry)
				AddTemplateIfNotExists(kvp.Key.ToString(), kvp.Value.Name, "Unit", kvp.Value.ModelPath, kvp.Value.VisualMode, existingIds);

		if (GameHost.BuildingRegistry != null)
			foreach (var kvp in GameHost.BuildingRegistry)
				AddTemplateIfNotExists(kvp.Key.ToString(), kvp.Value.Name, "Building", kvp.Value.ModelPath, kvp.Value.VisualMode, existingIds);

		if (GameHost.ResourceRegistry != null)
			foreach (var kvp in GameHost.ResourceRegistry)
				AddTemplateIfNotExists(kvp.Key.ToString(), kvp.Value.Name, "Resource", kvp.Value.ModelPath, kvp.Value.VisualMode, existingIds);

		if (GameHost.PropRegistry != null)
			foreach (var kvp in GameHost.PropRegistry)
				AddTemplateIfNotExists(kvp.Key.ToString(), kvp.Value.Name, "Prop", kvp.Value.ModelPath, kvp.Value.VisualMode, existingIds);
	}

	private void LoadMapAssetsTemplates(string wsPath, HashSet<string> existingIds)
	{
		try
		{
			var assets = MapAssetHelper.LoadAssets(wsPath);
			if (assets == null) return;

			foreach (var groupKvp in assets.GetAllCategories())
			{
				ProcessAssetGroup(groupKvp.Key, groupKvp.Value, existingIds);
			}
		}
		catch
		{
		}
	}

	private void ProcessAssetGroup(string groupName, Dictionary<string, string> subDict, HashSet<string> existingIds)
	{
		if (subDict == null) return;
		string category = groupName switch
		{
			"Character" or "units" or "characters" => "Unit",
			"Building" or "buildings" => "Building",
			"Resource" or "resources" => "Resource",
			_ => "Prop"
		};

		foreach (var itemKvp in subDict)
		{
			string fn = itemKvp.Key;
			if (string.IsNullOrEmpty(fn) || (!fn.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) && !fn.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))) continue;

			string id = Path.GetFileName(fn);
			AddTemplateIfNotExists(id, id, category, fn, null, existingIds);
		}
	}

	private void AddSampleTemplates()
	{
		_availableTemplates.Add(new TemplateItemInfo
		{
			Id = "(Sample Tree Cylinder)",
			DisplayName = "(Sample Tree Cylinder)",
			Category = "Sample",
			ModelPath = "",
			VisualMode = "Mesh"
		});
		_availableTemplates.Add(new TemplateItemInfo
		{
			Id = "(Sample Unit Capsule)",
			DisplayName = "(Sample Unit Capsule)",
			Category = "Sample",
			ModelPath = "",
			VisualMode = "Mesh"
		});
	}

	private void PopulateTemplatePickerAndSelect()
	{
		int selectedIdx = FindSelectedTemplateIndex();

		for (int i = 0; i < _availableTemplates.Count; i++)
		{
			var t = _availableTemplates[i];
			string label = !string.IsNullOrEmpty(t.DisplayName) && !string.Equals(t.DisplayName, t.Id, StringComparison.OrdinalIgnoreCase)
				? $"[{t.Category}] {t.DisplayName} ({t.Id})"
				: $"[{t.Category}] {t.Id}";
			t.FormattedLabel = label;
			_optTemplatePicker.AddItem(label, i);
		}

		if (selectedIdx >= 0 && selectedIdx < _availableTemplates.Count)
		{
			_selectedTemplateKey = _availableTemplates[selectedIdx].Id;
			_optTemplatePicker.Selected = selectedIdx;
		}
		else if (_availableTemplates.Count > 0)
		{
			_selectedTemplateKey = _availableTemplates[0].Id;
			_optTemplatePicker.Selected = 0;
		}
	}

}