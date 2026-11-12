using System;
using Godot;
using Realm.Shared.Metadata;

namespace Realm.Client.UI.MapEditor;

/// <summary>
///     Edits one upgrade template. An upgrade carries only its identity, its icon and the text that describes it - it
///     changes no model and grants nothing - so this dialog holds nothing beyond what every template has.
/// </summary>
[GlobalClass]
public partial class UpgradeEditDialog : IconTemplateEditDialogBase
{
	public UpgradeEditDialog() { }

	public UpgradeEditDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Edit Upgrade"), new Vector2(480, 420), "upgrades", "upgrade")
	{
	}

	protected override void BuildCategorySections(VBoxContainer content)
	{
	}

	protected override void LoadTemplateProperties(MapMetadata meta, string templateId)
	{
		var upgrade = meta.GetUpgrade(templateId) ?? meta.GetUpgrade(_slug);
		if (upgrade == null) return;

		_name = string.IsNullOrEmpty(upgrade.Name) ? _slug : upgrade.Name;
		_iconPath = FormatIconPath(upgrade.IconPath);
		_tooltip = upgrade.Description ?? string.Empty;
	}

	protected override void ApplyTemplateProperties(MapMetadata meta, string newTemplateID)
	{
		bool updated = meta.UpdateUpgrade(_originalTemplateID, upgrade =>
		{
			upgrade.TemplateID = newTemplateID;
			upgrade.Name = _name;
			upgrade.Description = _tooltip;
			upgrade.IconPath = _iconPath;
			return upgrade;
		});

		if (!updated)
		{
			meta.AddOrUpdateUpgrade(new UpgradeMetadata
			{
				TemplateID = newTemplateID,
				Name = !string.IsNullOrEmpty(_name) ? _name : _slug,
				Description = _tooltip,
				IconPath = _iconPath
			});
		}

		if (!string.Equals(_originalTemplateID, newTemplateID, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_originalTemplateID))
		{
			meta.RemoveUpgrade(_originalTemplateID);
		}
	}

	protected override bool TemplateIdExists(MapMetadata meta, string fullId)
	{
		return meta.GetUpgrade(fullId) != null;
	}
}
