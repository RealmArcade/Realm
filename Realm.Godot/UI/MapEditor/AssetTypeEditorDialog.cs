using Godot;
using System;
using Realm.Shared.Metadata;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

public partial class AssetTypeEditorDialog : FloatingDialogBase
{
	private readonly IndexedAsset _asset;
	private readonly Action<string> _onSaveCallback;
	private Label _lblPrompt;
	private OptionButton _optAssetType;
	private ReadOnlySet<string> _validTypes = new HashSet<string>().AsReadOnly();

	public AssetTypeEditorDialog(MapEditorHUD hud, IndexedAsset asset, Action<string> onSaveCallback)
		: base(hud, string.Format(TranslationServer.Translate("Edit Asset Type - {0}"), asset?.FileName ?? string.Empty), new Vector2(380, 200))
	{
		_asset = asset;
		_onSaveCallback = onSaveCallback;

		BuildControls();
	}

	private void BuildControls()
	{
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 10);
		BodyContainer.AddChild(contentVBox);

		AddSectionHeader(contentVBox, "🏷 " + TranslationServer.Translate("SELECT ASSET TYPE"), new Color(0.35f, 0.75f, 0.9f));

		_lblPrompt = new Label();
		_lblPrompt.AddThemeFontSizeOverride("font_size", 11);
		_lblPrompt.AutowrapMode = TextServer.AutowrapMode.Word;
		_lblPrompt.Text = string.Format(
			TranslationServer.Translate("Select embedded Asset Type for '{0}':"),
			_asset?.FileName ?? string.Empty
		);
		contentVBox.AddChild(_lblPrompt);

		var rowType = new HBoxContainer();
		rowType.AddThemeConstantOverride("separation", 8);

		var lblType = new Label();
		lblType.Text = TranslationServer.Translate("Asset Type:");
		lblType.CustomMinimumSize = new Vector2(100, 0);
		lblType.AddThemeFontSizeOverride("font_size", 11);
		rowType.AddChild(lblType);

		_optAssetType = new OptionButton();
		_optAssetType.AddThemeFontSizeOverride("font_size", 11);
		_optAssetType.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

		if (_asset != null)
		{
			_validTypes = RealmMetadataHelper.GetValidAssetTypesForExtension(_asset.FilePath);
			string currentType = RealmMetadataHelper.ExtractAssetType(_asset.FilePath) ?? string.Empty;

			var validTypesArray = _validTypes.ToArray();
			for (int i = 0; i < validTypesArray.Length; i++)
			{
				string typeName = validTypesArray[i];
				_optAssetType.AddItem(typeName, i);
				_optAssetType.SetItemMetadata(i, typeName);
				if (typeName.Equals(currentType, StringComparison.OrdinalIgnoreCase))
				{
					_optAssetType.Selected = i;
				}
			}

			if (_optAssetType.Selected < 0 && _validTypes.Count > 0)
			{
				_optAssetType.Selected = 0;
			}
		}

		rowType.AddChild(_optAssetType);
		contentVBox.AddChild(rowType);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("SAVE");
	}

	protected override void OnApply()
	{
		int sel = _optAssetType.Selected;
		if (sel >= 0 && sel < _optAssetType.ItemCount && _asset != null)
		{
			string selectedType = _optAssetType.GetItemMetadata(sel).AsString();
			bool success = RealmMetadataHelper.SetAssetType(_asset.FilePath, selectedType);
			if (success)
			{
				_onSaveCallback?.Invoke(selectedType);
			}
			else
			{
				Hud?.ShowFeedback(TranslationServer.Translate("Failed to update embedded asset type."));
			}
		}
		CloseDialog();
	}
}
