using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Shared.Metadata;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class ReplaceObjectDialog : FloatingDialogBase
{
	public ReplaceObjectDialog() { }

	private OptionButton _optSource = null!;
	private OptionButton _optTarget = null!;
	private CheckBox _chkSwap = null!;

	private string _lastSourceId = string.Empty;
	private string _lastTargetId = string.Empty;

	public ReplaceObjectDialog(MapEditorHUD hud) : base(hud, TranslationServer.Translate("SWAP OR REPLACE OBJECTS"), new Vector2(460, 260))
	{
		BuildUI();
	}

	private void BuildUI()
	{
		var contentVBox = CreateContentColumn();
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, "🔀 " + TranslationServer.Translate("OBJECT SWAP SELECTION"), new Color(0.35f, 0.75f, 0.9f));

		var lblInfo = new Label();
		lblInfo.Name = "LblInfo_Label";
		lblInfo.Text = TranslationServer.Translate("Pick a source object and a target object. Swapping exchanges the two wherever the map places them; with swap cleared every source instance becomes the target.");
		lblInfo.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblInfo.CustomMinimumSize = new Vector2(400, 0);
		lblInfo.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblInfo.AddThemeFontSizeOverride("font_size", 11);
		lblInfo.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		contentVBox.AddChild(lblInfo);

		BuildSelectionRow(contentVBox, "RowSource_HBoxContainer", "LblSource_Label", "OptSource_OptionButton", "Source Object:", out _optSource);
		BuildSelectionRow(contentVBox, "RowTarget_HBoxContainer", "LblTarget_Label", "OptTarget_OptionButton", "Target Object:", out _optTarget);

		_chkSwap = new CheckBox();
		_chkSwap.Name = "ChkSwap_CheckBox";
		_chkSwap.Text = TranslationServer.Translate("Swap source and target");
		_chkSwap.ButtonPressed = true;
		_chkSwap.FocusMode = FocusModeEnum.None;
		_chkSwap.AddThemeFontSizeOverride("font_size", 11);
		_chkSwap.TooltipText = TranslationServer.Translate("Swap the two objects with each other instead of moving every source instance onto the target");
		UIStyle.ApplyCheckboxStyle(_chkSwap);
		contentVBox.AddChild(_chkSwap);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("APPLY");
	}

	private static void BuildSelectionRow(Control parent, string rowName, string labelName, string optionName, string labelText, out OptionButton option)
	{
		var row = new HBoxContainer();
		row.Name = rowName;
		row.AddThemeConstantOverride("separation", 8);

		var label = new Label();
		label.Name = labelName;
		label.Text = TranslationServer.Translate(labelText);
		label.CustomMinimumSize = new Vector2(120, 0);
		label.AddThemeFontSizeOverride("font_size", 11);
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		row.AddChild(label);

		option = new OptionButton();
		option.Name = optionName;
		option.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		option.CustomMinimumSize = new Vector2(0, 26);
		option.AddThemeFontSizeOverride("font_size", 11);
		option.FocusMode = FocusModeEnum.None;
		row.AddChild(option);

		parent.AddChild(row);
	}

	public override void OpenDialog()
	{
		PopulateDropdowns();
		base.OpenDialog();
	}

	private void PopulateDropdowns()
	{
		if (_optSource == null || _optTarget == null) return;

		_optSource.Clear();
		_optTarget.Clear();

		List<string> objectIds = CollectObjectIds();
		for (int index = 0; index < objectIds.Count; index++)
		{
			string objectId = objectIds[index];
			_optSource.AddItem(objectId, index);
			_optSource.SetItemMetadata(index, objectId);
			_optTarget.AddItem(objectId, index);
			_optTarget.SetItemMetadata(index, objectId);
		}

		SelectObject(_optSource, _lastSourceId, 0);
		SelectObject(_optTarget, _lastTargetId, objectIds.Count > 1 ? 1 : 0);
	}

	private static void SelectObject(OptionButton option, string objectId, int fallbackIndex)
	{
		if (option.ItemCount <= 0) return;

		int selected = fallbackIndex;
		if (!string.IsNullOrEmpty(objectId))
		{
			for (int index = 0; index < option.ItemCount; index++)
			{
				if (string.Equals(option.GetItemText(index), objectId, StringComparison.OrdinalIgnoreCase))
				{
					selected = index;
					break;
				}
			}
		}

		option.Selected = Math.Clamp(selected, 0, option.ItemCount - 1);
	}

	private static List<string> CollectObjectIds()
	{
		var objectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		string workspacePath = MapWorkspaceService.GetActiveWorkspacePath();
		if (!string.IsNullOrWhiteSpace(workspacePath) && GameContext.MetadataService.TryLoadMetadata(workspacePath, out var metadata) && metadata != null)
		{
			AddMetadataIds(objectIds, metadata);
		}

		AddPlacedIds(objectIds);

		return objectIds
			.OrderBy(TypeOf, StringComparer.OrdinalIgnoreCase)
			.ThenBy(NameOf, StringComparer.OrdinalIgnoreCase)
			.ThenBy(objectId => objectId, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static void AddMetadataIds(HashSet<string> objectIds, MapMetadata metadata)
	{
		foreach (UnitMetadata unit in metadata.Templates.Units) AddId(objectIds, unit.TemplateID);
		foreach (UnitMetadata building in metadata.Templates.Buildings) AddId(objectIds, building.TemplateID);
		foreach (ResourceMetadata resource in metadata.Templates.Resources) AddId(objectIds, resource.TemplateID);
		foreach (PropMetadata prop in metadata.Templates.Props) AddId(objectIds, prop.TemplateID);
		foreach (ItemMetadata item in metadata.Templates.Items) AddId(objectIds, item.TemplateID);
		foreach (ItemMetadata consumable in metadata.Templates.Consumables) AddId(objectIds, consumable.TemplateID);
		foreach (VfxAttachmentConfig vfx in metadata.Templates.Vfx) AddId(objectIds, vfx.VfxId);
		foreach (string decalId in metadata.Decals.Keys) AddId(objectIds, decalId);
	}

	private static void AddPlacedIds(HashSet<string> objectIds)
	{
		var host = GameContext.Host;
		if (host == null) return;

		foreach (Unit3D unit in host.AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit)) AddId(objectIds, unit.UnitId);
		}

		foreach (Prop3D prop in host.AllProps)
		{
			if (GodotObject.IsInstanceValid(prop)) AddId(objectIds, prop.PropId);
		}

		foreach (Decal decal in host.AllDecals)
		{
			if (GodotObject.IsInstanceValid(decal)) AddId(objectIds, decal is Decal3D decal3D ? decal3D.DecalId : "logo");
		}

		foreach (LootItem3D item in host.AllLootItems)
		{
			if (GodotObject.IsInstanceValid(item)) AddId(objectIds, item.ItemTypeId);
		}

		foreach (ProceduralVfxInstance3D vfx in host.AllVfx)
		{
			if (GodotObject.IsInstanceValid(vfx)) AddId(objectIds, vfx.Config?.VfxId);
		}
	}

	private static void AddId(HashSet<string> objectIds, string? objectId)
	{
		if (string.IsNullOrWhiteSpace(objectId)) return;
		objectIds.Add(objectId);
	}

	private static string TypeOf(string objectId)
	{
		int separator = objectId.IndexOf('/');
		return separator > 0 ? objectId[..separator] : objectId;
	}

	private static string NameOf(string objectId)
	{
		int separator = objectId.IndexOf('/');
		return separator >= 0 && separator + 1 < objectId.Length ? objectId[(separator + 1)..] : string.Empty;
	}

	protected override void OnApply()
	{
		string sourceId = ReadObjectId(_optSource);
		string targetId = ReadObjectId(_optTarget);
		bool swap = _chkSwap.ButtonPressed;

		ObjectSwapOutcome outcome = EditorObjectSwap.Apply(sourceId, targetId, swap);
		if (!outcome.Success)
		{
			Hud?.ShowFeedback(DescribeFailure(outcome.Failure, sourceId));
			return;
		}

		_lastSourceId = sourceId;
		_lastTargetId = targetId;

		string message = swap
			? string.Format(TranslationServer.Translate("Swapped {0} objects of {1} with {2}"), outcome.ChangedObjects, sourceId, targetId)
			: string.Format(TranslationServer.Translate("Replaced {0} objects of {1} with {2}"), outcome.ChangedObjects, sourceId, targetId);
		Hud?.ShowFeedback(message);
	}

	private static string ReadObjectId(OptionButton option)
	{
		if (option.ItemCount <= 0 || option.Selected < 0 || option.Selected >= option.ItemCount) return string.Empty;

		Variant metadata = option.GetItemMetadata(option.Selected);
		return metadata.VariantType == Variant.Type.Nil ? string.Empty : metadata.AsString();
	}

	private static string DescribeFailure(ObjectSwapFailure failure, string sourceId)
	{
		return failure switch
		{
			ObjectSwapFailure.MissingSelection => TranslationServer.Translate("Choose a source object and a target object first."),
			ObjectSwapFailure.SameObject => TranslationServer.Translate("Choose two different objects."),
			ObjectSwapFailure.NoTerrain => TranslationServer.Translate("The editor has no terrain whose objects could be changed."),
			ObjectSwapFailure.NoInstances => string.Format(TranslationServer.Translate("The map places no instances of {0}."), sourceId),
			_ => TranslationServer.Translate("The objects could not be changed.")
		};
	}
}
