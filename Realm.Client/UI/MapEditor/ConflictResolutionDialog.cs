using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Distribution;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class ConflictResolutionDialog : FloatingDialogBase
{
	private readonly List<MapReferenceAssetEntry> _assets = new();
	private readonly List<string> _filteredAssetPaths = new();
	private MapReferences _references;

	private LineEdit _txtSearch;
	private OptionButton _optAsset;
	private VBoxContainer _priorityList;
	private Label _lblStatus;
	private int _rowCount;
	private Action _onClosed;

	public ConflictResolutionDialog() { }

	public ConflictResolutionDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Conflict Resolution"), new Vector2(860, 620))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.webp", 30, 45, 70, 70);
		BuildControls();
		SetFooterApplyOnly(TranslationServer.Translate("APPLY"));
	}

	private void BuildControls()
	{
		BodyContainer.AddThemeConstantOverride("separation", 8);
		AddSectionHeader(BodyContainer, TranslationServer.Translate("ASSET CONFLICT RESOLUTION"));
		AddDescription(BodyContainer, TranslationServer.Translate("A file carried by more than one map is kept whole from exactly one of them. A file with no entry here is kept from this map when it carries the file, and from the only referenced map that carries it otherwise. metadata.json is always merged and never appears here."));

		var searchRow = new HBoxContainer();
		searchRow.Name = "SearchRow_HBoxContainer";
		searchRow.AddThemeConstantOverride("separation", 8);
		BodyContainer.AddChild(searchRow);

		_txtSearch = new LineEdit();
		_txtSearch.Name = "TxtSearch_LineEdit";
		_txtSearch.PlaceholderText = TranslationServer.Translate("Type to search the assets of every referenced map");
		_txtSearch.CustomMinimumSize = new Vector2(240, 26);
		_txtSearch.AddThemeFontSizeOverride("font_size", 11);
		_txtSearch.TextChanged += _ => RefreshAssetOptions();
		searchRow.AddChild(_txtSearch);

		_optAsset = new OptionButton();
		_optAsset.Name = "OptAsset_OptionButton";
		_optAsset.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optAsset.CustomMinimumSize = new Vector2(0, 26);
		_optAsset.AddThemeFontSizeOverride("font_size", 11);
		_optAsset.Set("icon_max_width", 0);
		_optAsset.FocusMode = FocusModeEnum.None;
		_optAsset.TooltipText = TranslationServer.Translate("The asset to state a conflict priority for");
		searchRow.AddChild(_optAsset);

		AddButton(searchRow, TranslationServer.Translate("+ Add"), OnAddPressed, TranslationServer.Translate("Add the selected asset as a conflict priority"), 11, new Vector2(80, 26));

		var listPanel = new PanelContainer();
		listPanel.Name = "PriorityPanel_PanelContainer";
		listPanel.CustomMinimumSize = new Vector2(0, 340);
		listPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		listPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(listPanel);

		var scroll = new ScrollContainer();
		scroll.Name = "PriorityScroll_ScrollContainer";
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		listPanel.AddChild(scroll);

		_priorityList = new VBoxContainer();
		_priorityList.Name = "PriorityList_VBoxContainer";
		_priorityList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_priorityList.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(_priorityList);

		_lblStatus = new Label();
		_lblStatus.Name = "Status_Label";
		_lblStatus.AutowrapMode = TextServer.AutowrapMode.Word;
		_lblStatus.CustomMinimumSize = new Vector2(780, 0);
		_lblStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblStatus.AddThemeFontSizeOverride("font_size", 10);
		_lblStatus.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		BodyContainer.AddChild(_lblStatus);
	}

	public void OpenForWorkspace(Action onClosed = null)
	{
		_onClosed = onClosed;
		_lblStatus.Text = string.Empty;
		_txtSearch.Text = string.Empty;

		string workspacePath = ResolveWorkspacePath();
		_references = GameContext.MapReferenceService.Load(workspacePath);
		if (_references == null)
		{
			_references = new MapReferences();
		}

		_references.ConflictPriorities ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
		_assets.Clear();
		_assets.AddRange(GameContext.MapReferenceService.DescribeAssets(workspacePath, _references));
		RefreshAssetOptions();
		RebuildPriorityRows();
		OpenDialog();
	}

	public override void CloseDialog()
	{
		base.CloseDialog();
		_onClosed?.Invoke();
	}

	private static string ResolveWorkspacePath()
	{
		return !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory)
			? GameContext.EditorService._editingDirectory
			: MapWorkspaceService.GetActiveWorkspacePath();
	}

	private void RefreshAssetOptions()
	{
		_optAsset.Clear();
		_filteredAssetPaths.Clear();

		string query = _txtSearch.Text?.Trim() ?? string.Empty;
		foreach (MapReferenceAssetEntry asset in _assets)
		{
			if (query.Length > 0 && asset.RelativePath.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
			{
				continue;
			}

			int index = _filteredAssetPaths.Count;
			_filteredAssetPaths.Add(asset.RelativePath);
			_optAsset.AddItem(asset.RelativePath, index);
			_optAsset.SetItemTooltip(index, BuildCarriersTooltip(asset));
		}

		if (_filteredAssetPaths.Count == 0)
		{
			_optAsset.AddItem(TranslationServer.Translate("No matching asset"), -1);
			_optAsset.Disabled = true;
			return;
		}

		_optAsset.Disabled = false;
		_optAsset.Selected = 0;
	}

	private static string BuildCarriersTooltip(MapReferenceAssetEntry asset)
	{
		string carriers = string.Join(", ", asset.CarryingMapNames.Select(name => string.IsNullOrEmpty(name)
			? (string)TranslationServer.Translate("this map")
			: name));

		return string.Format(TranslationServer.Translate("Carried by: {0}"), carriers);
	}

	private void OnAddPressed()
	{
		int selectedId = _optAsset.GetSelectedId();
		if (selectedId < 0 || selectedId >= _filteredAssetPaths.Count)
		{
			return;
		}

		string assetPath = _filteredAssetPaths[selectedId];
		MapReferenceAssetEntry asset = _assets.FirstOrDefault(entry => string.Equals(entry.RelativePath, assetPath, StringComparison.OrdinalIgnoreCase));
		if (asset == null)
		{
			return;
		}

		if (_references.TryGetConflictPriority(assetPath, out _))
		{
			_lblStatus.Text = TranslationServer.Translate("That asset already has a priority.");
			return;
		}

		string defaultMap = asset.CarryingMapNames.FirstOrDefault(name => !string.IsNullOrEmpty(name) && _references.FindReference(name) != null);
		if (string.IsNullOrEmpty(defaultMap))
		{
			_lblStatus.Text = TranslationServer.Translate("This map is the only map carrying that asset, so it needs no priority.");
			return;
		}

		_references.SetConflictPriority(assetPath, defaultMap);
		_lblStatus.Text = string.Empty;
		RebuildPriorityRows();
	}

	private void RebuildPriorityRows()
	{
		foreach (Node child in _priorityList.GetChildren())
		{
			_priorityList.RemoveChild(child);
			child.QueueFree();
		}

		_rowCount = 0;

		if (_references.ConflictPriorities.Count == 0)
		{
			var emptyLabel = new Label();
			emptyLabel.Name = "EmptyPriority_Label";
			emptyLabel.Text = TranslationServer.Translate("No priorities yet. Every colliding file is kept from this map.");
			emptyLabel.AddThemeFontSizeOverride("font_size", 11);
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			_priorityList.AddChild(emptyLabel);
			return;
		}

		foreach (KeyValuePair<string, string?> priority in _references.ConflictPriorities.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase).ToList())
		{
			AddPriorityRow(priority.Key, priority.Value);
		}
	}

	private void AddPriorityRow(string assetPath, string mapName)
	{
		var row = new PanelContainer();
		row.Name = $"PriorityRow_{_rowCount}_PanelContainer";
		_rowCount++;
		row.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

		var rowBox = new HBoxContainer();
		rowBox.Name = "PriorityRowHBox_HBoxContainer";
		rowBox.AddThemeConstantOverride("separation", 8);
		row.AddChild(rowBox);

		var label = new Label();
		label.Name = "PriorityAsset_Label";
		label.Text = assetPath;
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.CustomMinimumSize = new Vector2(480, 0);
		label.ClipText = true;
		label.AddThemeFontSizeOverride("font_size", 10);

		MapReferenceAssetEntry asset = _assets.FirstOrDefault(entry => string.Equals(entry.RelativePath, assetPath, StringComparison.OrdinalIgnoreCase));
		label.TooltipText = asset != null
			? BuildCarriersTooltip(asset)
			: assetPath;
		rowBox.AddChild(label);

		var dropdown = new OptionButton();
		dropdown.Name = "PriorityMap_OptionButton";
		dropdown.CustomMinimumSize = new Vector2(200, 26);
		dropdown.AddThemeFontSizeOverride("font_size", 11);
		dropdown.Set("icon_max_width", 0);
		dropdown.FocusMode = FocusModeEnum.None;

		int selectedIndex = 0;
		for (int index = 0; index < _references.References.Count; index++)
		{
			MapReference reference = _references.References[index];
			dropdown.AddItem(reference.MapName, index);
			if (string.Equals(reference.MapName, mapName, StringComparison.OrdinalIgnoreCase))
			{
				selectedIndex = index;
			}
		}

		dropdown.Selected = selectedIndex;
		dropdown.ItemSelected += chosen =>
		{
			int chosenIndex = (int)chosen;
			if (chosenIndex < 0 || chosenIndex >= _references.References.Count)
			{
				return;
			}

			_references.SetConflictPriority(assetPath, _references.References[chosenIndex].MapName);
		};
		rowBox.AddChild(dropdown);

		var removeButton = AddButton(
			rowBox,
			UnicodeIcons.TRASH,
			() => RemovePriority(assetPath),
			TranslationServer.Translate("Keep this file from this map instead"),
			13,
			new Vector2(34, 26));
		removeButton.Name = "BtnRemovePriority_Button";

		_priorityList.AddChild(row);
	}

	private void RemovePriority(string assetPath)
	{
		_references.SetConflictPriority(assetPath, null);
		_lblStatus.Text = string.Empty;
		RebuildPriorityRows();
	}

	protected override void OnApply()
	{
		string workspacePath = ResolveWorkspacePath();
		if (string.IsNullOrEmpty(workspacePath))
		{
			_lblStatus.Text = TranslationServer.Translate("The editor workspace is not available.");
			return;
		}

		GameContext.MapReferenceService.Save(workspacePath, _references);
		MapReferenceResolutionResult result = GameContext.MapReferenceService.ApplyOnSave(workspacePath);
		if (!result.Success)
		{
			_lblStatus.Text = result.Message;
			Hud?.ShowFeedback(result.Message);
			return;
		}

		_lblStatus.Text = result.Message;
		Hud?.ShowFeedback(_lblStatus.Text);
	}
}
