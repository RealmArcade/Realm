using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Distribution;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class CompositeMapDialog : FloatingDialogBase
{
	private class AvailableMap
	{
		public string MapName { get; init; } = string.Empty;
		public string Version { get; init; } = "1.0.0";
		public string ManifestBlake3 { get; init; } = string.Empty;
		public string FolderPath { get; init; } = string.Empty;
	}

	private readonly List<AvailableMap> _available = new();
	private MapReferences _references;

	private OptionButton _optAvailable;
	private VBoxContainer _selectedList;
	private Label _lblStatus;
	private Button _btnAdd;
	private Button _btnConflictResolution;
	private int _rowCount;
	private ConflictResolutionDialog _conflictResolutionDialog;

	public CompositeMapDialog() { }

	public CompositeMapDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Composite Map"), new Vector2(720, 560))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_assets_importer.webp", 30, 45, 70, 70);
		BuildControls();
		SetFooterApplyOnly(TranslationServer.Translate("APPLY"));
	}

	private void BuildControls()
	{
		BodyContainer.AddThemeConstantOverride("separation", 8);
		AddSectionHeader(BodyContainer, TranslationServer.Translate("REFERENCED MAPS"));
		AddDescription(BodyContainer, TranslationServer.Translate("This map takes the files of the maps below into itself on every save, so it stays a normal map: it can be edited, tested and played, and its own files win wherever they collide. metadata.json is merged, every other file is kept from one map only."));

		var availableRow = new HBoxContainer();
		availableRow.Name = "AvailableRow_HBoxContainer";
		availableRow.AddThemeConstantOverride("separation", 8);
		BodyContainer.AddChild(availableRow);

		var lblAvailable = new Label();
		lblAvailable.Name = "LblAvailable_Label";
		lblAvailable.Text = TranslationServer.Translate("Referenced map:");
		lblAvailable.AddThemeFontSizeOverride("font_size", 11);
		lblAvailable.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		lblAvailable.VerticalAlignment = VerticalAlignment.Center;
		availableRow.AddChild(lblAvailable);

		_optAvailable = new OptionButton();
		_optAvailable.Name = "OptAvailable_OptionButton";
		_optAvailable.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_optAvailable.CustomMinimumSize = new Vector2(0, 26);
		_optAvailable.AddThemeFontSizeOverride("font_size", 11);
		_optAvailable.Set("icon_max_width", 0);
		_optAvailable.FocusMode = FocusModeEnum.None;
		_optAvailable.TooltipText = TranslationServer.Translate("Choose a map package this map should take content from");
		availableRow.AddChild(_optAvailable);

		_btnAdd = CreateActionButton("BtnAdd_Button", TranslationServer.Translate("Add"), OnAddPressed, TranslationServer.Translate("Reference the selected map"), 70);
		availableRow.AddChild(_btnAdd);

		var listPanel = new PanelContainer();
		listPanel.Name = "SelectedPanel_PanelContainer";
		listPanel.CustomMinimumSize = new Vector2(0, 220);
		listPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
		listPanel.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());
		BodyContainer.AddChild(listPanel);

		var scroll = new ScrollContainer();
		scroll.Name = "SelectedScroll_ScrollContainer";
		scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		listPanel.AddChild(scroll);

		_selectedList = new VBoxContainer();
		_selectedList.Name = "SelectedList_VBoxContainer";
		_selectedList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_selectedList.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(_selectedList);

		_lblStatus = new Label();
		_lblStatus.Name = "Status_Label";
		_lblStatus.AutowrapMode = TextServer.AutowrapMode.Word;
		_lblStatus.CustomMinimumSize = new Vector2(640, 0);
		_lblStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_lblStatus.AddThemeFontSizeOverride("font_size", 10);
		_lblStatus.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		BodyContainer.AddChild(_lblStatus);

		var actionRow = new HBoxContainer();
		actionRow.Name = "ActionRow_HBoxContainer";
		actionRow.Alignment = BoxContainer.AlignmentMode.End;
		actionRow.AddThemeConstantOverride("separation", 8);
		BodyContainer.AddChild(actionRow);

		_btnConflictResolution = CreateActionButton(
			"BtnConflictResolution_Button",
			TranslationServer.Translate("Conflict Resolution"),
			OnConflictResolutionPressed,
			TranslationServer.Translate("State which map keeps each file this map and its referenced maps both carry"),
			160);
		actionRow.AddChild(_btnConflictResolution);
	}

	private Button CreateActionButton(string nodeName, string text, Action onPressed, string tooltip, int minimumWidth)
	{
		var button = new Button();
		button.Name = nodeName;
		button.Set("icon_max_width", 0);
		button.Text = text;
		button.CustomMinimumSize = new Vector2(minimumWidth, 26);
		button.FocusMode = FocusModeEnum.None;
		button.TooltipText = tooltip;
		button.AddThemeFontSizeOverride("font_size", 11);
		button.Pressed += onPressed;
		return button;
	}

	public void OpenForWorkspace()
	{
		_lblStatus.Text = string.Empty;
		LoadAvailable();
		LoadReferences();
		RebuildRows();
		OpenDialog();
	}

	private static string ResolveWorkspacePath()
	{
		return !string.IsNullOrEmpty(GameContext.EditorService._editingDirectory)
			? GameContext.EditorService._editingDirectory
			: MapWorkspaceService.GetActiveWorkspacePath();
	}

	private void LoadReferences()
	{
		string workspacePath = ResolveWorkspacePath();
		_references = GameContext.MapReferenceService.Load(workspacePath);
		if (_references == null)
		{
			_references = new MapReferences();
			return;
		}

		_references.References ??= new List<MapReference>();
		_references.ConflictPriorities ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

		if (_references.References.Count > 0)
		{
			_lblStatus.Text = string.Format(
				TranslationServer.Translate("This map takes content from {0} referenced map(s)."),
				_references.References.Count);
		}
	}

	private void LoadAvailable()
	{
		_available.Clear();
		_optAvailable.Clear();

		string parentMapName = Hud?.GetMapNameFromMetadata() ?? string.Empty;
		IReadOnlyList<DownloadedMapInfo> downloadedMaps = GameContext.MapStorageService.GetMapsAvailableForImport();

		foreach (DownloadedMapInfo downloadedMap in downloadedMaps)
		{
			if (string.Equals(downloadedMap.Title, parentMapName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			foreach (DownloadedMapVersionInfo version in downloadedMap.Versions)
			{
				if (string.IsNullOrEmpty(version.ManifestFilePath) || !File.Exists(version.ManifestFilePath))
				{
					continue;
				}

				string manifestDirectory = Path.GetDirectoryName(version.ManifestFilePath) ?? string.Empty;
				var available = new AvailableMap
				{
					MapName = downloadedMap.Title,
					Version = version.Version,
					ManifestBlake3 = version.ManifestHash,
					FolderPath = manifestDirectory
				};

				int index = _available.Count;
				_available.Add(available);
				_optAvailable.AddItem($"{available.MapName} v{available.Version}", index);
			}
		}

		_btnAdd.Disabled = _available.Count == 0;
		if (_available.Count == 0 && string.IsNullOrEmpty(_lblStatus.Text))
		{
			_lblStatus.Text = TranslationServer.Translate("No map packages are available to reference. Download or import maps into storage first.");
		}
	}

	private void OnAddPressed()
	{
		int selectedId = _optAvailable.GetSelectedId();
		if (selectedId < 0 || selectedId >= _available.Count)
		{
			return;
		}

		AvailableMap available = _available[selectedId];
		bool alreadyReferenced = _references.References.Any(reference =>
			string.Equals(reference.MapName, available.MapName, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(reference.Version, available.Version, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(
				ContentAddressableStorage.NormalizeBlake3Hash(reference.ManifestBlake3),
				ContentAddressableStorage.NormalizeBlake3Hash(available.ManifestBlake3),
				StringComparison.OrdinalIgnoreCase));

		if (alreadyReferenced)
		{
			_lblStatus.Text = TranslationServer.Translate("That map is already referenced.");
			return;
		}

		string helperFolder = string.Empty;
		if (!string.IsNullOrEmpty(available.FolderPath) && File.Exists(Path.Combine(available.FolderPath, "metadata.json")))
		{
			helperFolder = available.FolderPath;
		}

		_references.References.Add(new MapReference
		{
			MapName = available.MapName,
			Version = available.Version,
			ManifestBlake3 = available.ManifestBlake3,
			FolderPath = helperFolder
		});

		_lblStatus.Text = string.Empty;
		RebuildRows();
	}

	private void RebuildRows()
	{
		foreach (Node child in _selectedList.GetChildren())
		{
			_selectedList.RemoveChild(child);
			child.QueueFree();
		}

		_rowCount = 0;

		if (_references == null || _references.References.Count == 0)
		{
			var emptyLabel = new Label();
			emptyLabel.Name = "EmptySelection_Label";
			emptyLabel.Text = TranslationServer.Translate("No maps are referenced yet.");
			emptyLabel.AddThemeFontSizeOverride("font_size", 11);
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			_selectedList.AddChild(emptyLabel);
			if (_btnConflictResolution != null)
			{
				_btnConflictResolution.Disabled = true;
			}

			return;
		}

		if (_btnConflictResolution != null)
		{
			_btnConflictResolution.Disabled = false;
		}

		foreach (MapReference reference in _references.References)
		{
			var row = new PanelContainer();
			row.Name = $"ReferenceRow_{_rowCount}_PanelContainer";
			_rowCount++;
			row.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

			var rowBox = new HBoxContainer();
			rowBox.Name = "ReferenceRowHBox_HBoxContainer";
			rowBox.AddThemeConstantOverride("separation", 8);
			row.AddChild(rowBox);

			var label = new Label();
			label.Name = "ReferenceLabel_Label";
			label.Text = $"{reference.MapName} v{reference.Version}";
			label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			label.AddThemeFontSizeOverride("font_size", 11);
			label.TooltipText = string.IsNullOrWhiteSpace(reference.FolderPath)
				? TranslationServer.Translate("Read from the map store, pinned to the manifest hash it was added with.")
				: string.Format(TranslationServer.Translate("Read from the authoring folder {0}."), reference.FolderPath);
			rowBox.AddChild(label);

			var removeButton = CreateActionButton(
				"BtnRemoveReference_Button",
				TranslationServer.Translate("Remove"),
				() => RemoveReference(reference),
				TranslationServer.Translate("Stop referencing this map"),
				80);
			rowBox.AddChild(removeButton);

			_selectedList.AddChild(row);
		}
	}

	private void RemoveReference(MapReference reference)
	{
		_references.References.Remove(reference);
		_lblStatus.Text = string.Empty;
		RebuildRows();
	}

	private void OnConflictResolutionPressed()
	{
		if (_references == null || _references.References.Count == 0)
		{
			_lblStatus.Text = TranslationServer.Translate("Reference at least one map before resolving conflicts.");
			return;
		}

		GameContext.MapReferenceService.Save(ResolveWorkspacePath(), _references);

		if (_conflictResolutionDialog == null || !GodotObject.IsInstanceValid(_conflictResolutionDialog))
		{
			_conflictResolutionDialog = new ConflictResolutionDialog(Hud);
		}

		_conflictResolutionDialog.OpenForWorkspace(() =>
		{
			_references = GameContext.MapReferenceService.Load(ResolveWorkspacePath()) ?? new MapReferences();
			RebuildRows();
		});
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

		_lblStatus.Text = result.HasReferences ? result.Message : TranslationServer.Translate("This map no longer references another map.");
		Hud?.ShowFeedback(_lblStatus.Text);

		if (result.HasReferences)
		{
			foreach (string replacedFile in result.ReplacedFiles)
			{
				_lblStatus.Text += " " + string.Format(
					TranslationServer.Translate("Local changes to {0} were replaced by the referenced copy."),
					replacedFile);
			}
		}

		CloseDialog();
	}
}
