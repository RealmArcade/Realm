using Godot;
using Realm.Client.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Realm.Client.UI.MapEditor;

public partial class TagEditorDialog : Realm.Client.UI.MapEditor.FloatingDialogBase
{
	private readonly IndexedAsset _asset;
	private readonly Action<List<string>> _onSaveCallback;
	private readonly List<string> _workingTags = new();
	private LineEdit _txtNewTag;
	private Button _btnAddTag;
	private VBoxContainer _tagsListContainer;

	public TagEditorDialog(MapEditorHUD hud, IndexedAsset asset, Action<List<string>> onSaveCallback)
		: base(hud, string.Format(TranslationServer.Translate("Edit Tags - {0}"), asset?.FileName ?? string.Empty), new Vector2(380, 360))
	{
		_asset = asset;
		_onSaveCallback = onSaveCallback;

		if (_asset != null)
		{
			var embeddedTags = RealmMetadataHelper.ExtractTags(_asset.FilePath);
			if (embeddedTags.Count > 0)
			{
				_workingTags.AddRange(embeddedTags);
			}
			else if (_asset.Tags != null)
			{
				_workingTags.AddRange(_asset.Tags);
			}
		}

		BuildTagEditorControls();
		RebuildTagRows();
	}

	private void BuildTagEditorControls()
	{
		var addRow = new HBoxContainer();
		addRow.AddThemeConstantOverride("separation", 6);

		_txtNewTag = new LineEdit();
		_txtNewTag.PlaceholderText = TranslationServer.Translate("Enter tag name...");
		_txtNewTag.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtNewTag.AddThemeFontSizeOverride("font_size", 11);
		_txtNewTag.TextSubmitted += (_) => AddCurrentTag();
		addRow.AddChild(_txtNewTag);

		_btnAddTag = new Button();
		_btnAddTag.Set("icon_max_width", 0);
		_btnAddTag.Text = "+";
		_btnAddTag.CustomMinimumSize = new Vector2(28, 24);
		_btnAddTag.FocusMode = FocusModeEnum.None;
		_btnAddTag.TooltipText = TranslationServer.Translate("Add tag");
		_btnAddTag.Pressed += AddCurrentTag;
		addRow.AddChild(_btnAddTag);

		BodyContainer.AddChild(addRow);

		var scrollPanel = new PanelContainer();
		scrollPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;

		_tagsListContainer = new VBoxContainer();
		_tagsListContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_tagsListContainer.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(_tagsListContainer);

		scrollPanel.AddChild(scroll);
		BodyContainer.AddChild(scrollPanel);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("SAVE");
	}

	private void AddCurrentTag()
	{
		string rawText = _txtNewTag.Text?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(rawText))
		{
			return;
		}

		var splitTags = rawText
			.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
			.Select(t => t.Trim().ToLowerInvariant())
			.Where(t => !string.IsNullOrEmpty(t));

		foreach (var tag in splitTags)
		{
			if (!_workingTags.Contains(tag, StringComparer.OrdinalIgnoreCase))
			{
				_workingTags.Add(tag);
			}
		}

		_txtNewTag.Text = string.Empty;
		RebuildTagRows();
		_txtNewTag.GrabFocus();
	}

	private void RemoveTag(string tag)
	{
		_workingTags.RemoveAll(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase));
		RebuildTagRows();
	}

	private void RebuildTagRows()
	{
		foreach (var child in _tagsListContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_workingTags.Count == 0)
		{
			var lblEmpty = new Label();
			lblEmpty.Text = TranslationServer.Translate("No tags assigned yet. Use the input above to add tags.");
			lblEmpty.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			lblEmpty.AddThemeFontSizeOverride("font_size", 10);
			lblEmpty.HorizontalAlignment = HorizontalAlignment.Center;
			lblEmpty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			_tagsListContainer.AddChild(lblEmpty);
			return;
		}

		foreach (var tag in _workingTags)
		{
			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);

			var lblTag = new Label();
			lblTag.Text = "🏷 " + tag;
			lblTag.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			lblTag.AddThemeFontSizeOverride("font_size", 11);
			lblTag.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			row.AddChild(lblTag);

			var btnRemove = new Button();
			btnRemove.Set("icon_max_width", 0);
			btnRemove.Text = "✕";
			btnRemove.CustomMinimumSize = new Vector2(22, 20);
			btnRemove.AddThemeFontSizeOverride("font_size", 9);
			btnRemove.FocusMode = FocusModeEnum.None;
			btnRemove.TooltipText = TranslationServer.Translate("Remove tag");
			string capturedTag = tag;
			btnRemove.Pressed += () => RemoveTag(capturedTag);
			row.AddChild(btnRemove);

			_tagsListContainer.AddChild(row);
		}
	}

	protected override void OnApply()
	{
		var finalTags = _workingTags
			.Select(t => t.Trim().ToLowerInvariant())
			.Where(t => !string.IsNullOrEmpty(t))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (_asset != null && File.Exists(_asset.FilePath))
		{
			bool success = RealmMetadataHelper.SetTags(_asset.FilePath, finalTags);
			if (success)
			{
				_onSaveCallback?.Invoke(finalTags);
			}
			else
			{
				Hud?.ShowFeedback(TranslationServer.Translate("Failed to update embedded asset tags."));
			}
		}
		else
		{
			_onSaveCallback?.Invoke(finalTags);
		}

		CloseDialog();
	}
}