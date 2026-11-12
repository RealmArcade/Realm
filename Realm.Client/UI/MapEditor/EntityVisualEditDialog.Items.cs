using System;
using Godot;
using Realm.Shared.Metadata;

namespace Realm.Client.UI.MapEditor;

/// <summary>
///     The item half of the visual inspector. A drop lying on the terrain has no transform, rig, collision body or
///     spawn/death shader of its own - the placement decides where it lies - so for an item the dialog keeps only what
///     decides how the drop is drawn: the model the item template states, its display name and its description.
/// </summary>
public partial class EntityVisualEditDialog
{
	private const string ItemCategoryName = "items";

	private bool IsItemTemplate => string.Equals(_category, ItemCategoryName, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	///     Shows only the controls an item template answers, so a map author picking a model for a drop is not offered
	///     sliders that write overrides the drop never reads.
	/// </summary>
	private void ApplyItemCategoryLayout()
	{
		bool isItem = IsItemTemplate;

		if (_visualModeRow != null) _visualModeRow.Visible = !isItem;
		if (_txtPortraitModelPath != null && _txtPortraitModelPath.GetParent() is Control portraitRow) portraitRow.Visible = !isItem;
		if (_transformSection != null) _transformSection.Visible = !isItem;
		if (_shaderSection != null) _shaderSection.Visible = !isItem;
		if (_lblDescription != null) _lblDescription.Text = isItem
			? TranslationServer.Translate("Choose the 3D model this item is drawn with on the terrain. Leave the model empty to draw the item's gem placeholder.")
			: TranslationServer.Translate("Modify visual, collision, and procedural motion settings for this entity or model.");
	}

	/// <summary>
	///     Reads the item template this drop was placed from. An item that states no model keeps an empty model path, so
	///     the drop falls back to its gem placeholder rather than borrowing a model from somewhere else.
	/// </summary>
	/// <param name="item">The item template the dialog is editing.</param>
	private void LoadFromItemMetadata(ItemMetadata item)
	{
		if (!string.IsNullOrEmpty(item.Name)) _name = item.Name;
		_description = item.Description ?? string.Empty;
		_modelPath = item.ModelPath ?? string.Empty;
		_visualMode = DetermineVisualMode();
	}

	/// <summary>
	///     Writes what the item dialog owns back into the item template, which is what the placed drop and the authority
	///     both read the model from. Every other item property is left exactly as the map states it.
	/// </summary>
	/// <param name="meta">The map metadata being saved.</param>
	/// <param name="newTemplateID">The template id, which is the slug the dialog now shows.</param>
	private void ApplyItemMetadata(MapMetadata meta, string newTemplateID)
	{
		string? modelPath = string.IsNullOrWhiteSpace(_modelPath) ? null : _modelPath;

		bool updated = meta.UpdateItem(_originalTemplateID, item =>
		{
			item.TemplateID = newTemplateID;
			item.Name = !string.IsNullOrEmpty(_name) ? _name : _slug;
			item.Description = _description;
			item.ModelPath = modelPath;
			return item;
		});

		if (!updated)
		{
			meta.AddOrUpdateItem(new ItemMetadata
			{
				TemplateID = newTemplateID,
				Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
				Description = _description,
				ModelPath = modelPath
			});
		}

		if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
		{
			meta.RemoveItem(_originalTemplateID);
		}
	}
}
