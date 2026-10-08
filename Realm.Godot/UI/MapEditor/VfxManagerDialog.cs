using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Godot.Utils;
using Realm.Godot.VFX;
using Realm.Godot.Services;

public partial class VfxManagerDialog : FloatingPreview3DDialogBase
{
	private ProceduralVfxInstance3D? _previewVfxInstance;

	private Label _lblSelectedVfxId;
	private Label _lblSelectedVfxName;
	private LineEdit _txtSearchFilter;
	private VBoxContainer _vfxListContainer;

	private string _selectedVfxId = string.Empty;
	private VfxAttachmentConfig? _selectedConfig;
	private Action<VfxAttachmentConfig>? _onSelectedCallback;

	public VfxManagerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("VFX Studio Manager"), new Vector2(580, 720))
	{
		DefaultDistance = 4.0f;
		CameraDistance = 4.0f;
		DefaultYaw = 0f;
		DefaultPitch = 0.25f;
		CameraYaw = 0f;
		CameraPitch = 0.25f;
		DefaultTargetPosition = new Vector3(0f, 0.5f, 0f);
		TargetPosition = DefaultTargetPosition;

		BuildControls();
		SetFooterCloseOnly();
	}

	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(530, 220));

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		AddCameraPresetToolbar(topControlsVBox, includeBack: false);

		var infoRow = new HBoxContainer();
		infoRow.AddThemeConstantOverride("separation", 8);

		var lblIdTitle = new Label { Text = TranslationServer.Translate("VFX ID:") };
		lblIdTitle.AddThemeFontSizeOverride("font_size", 11);
		lblIdTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		infoRow.AddChild(lblIdTitle);

		_lblSelectedVfxId = new Label { Text = "-" };
		_lblSelectedVfxId.AddThemeFontSizeOverride("font_size", 11);
		_lblSelectedVfxId.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		infoRow.AddChild(_lblSelectedVfxId);

		var lblNameTitle = new Label { Text = TranslationServer.Translate("Name:") };
		lblNameTitle.AddThemeFontSizeOverride("font_size", 11);
		lblNameTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		infoRow.AddChild(lblNameTitle);

		_lblSelectedVfxName = new Label { Text = "-" };
		_lblSelectedVfxName.AddThemeFontSizeOverride("font_size", 11);
		_lblSelectedVfxName.AddThemeColorOverride("font_color", Colors.White);
		infoRow.AddChild(_lblSelectedVfxName);

		topControlsVBox.AddChild(infoRow);

		AddSectionHeader(BodyContainer, "✨ " + TranslationServer.Translate("CONFIGURED VFX PRESETS & EFFECTS"));

		var toolbarRow = new HBoxContainer();
		toolbarRow.AddThemeConstantOverride("separation", 8);

		_txtSearchFilter = new LineEdit
		{
			PlaceholderText = TranslationServer.Translate("Filter VFX by ID or name..."),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			CustomMinimumSize = new Vector2(0, 26)
		};
		_txtSearchFilter.AddThemeFontSizeOverride("font_size", 11);
		_txtSearchFilter.TextChanged += (text) => RebuildVfxListUI();
		toolbarRow.AddChild(_txtSearchFilter);

		var btnNewVfx = AddButton(toolbarRow, "➕ " + TranslationServer.Translate("New VFX"), () =>
		{
			string newId = $"vfx_custom_{DateTime.UtcNow.Ticks % 100000}";
			var newCfg = new VfxAttachmentConfig
			{
				VfxId = newId,
				Name = "Custom VFX",
				PrimitiveType = VfxPrimitiveType.VortexDisc,
				BlendMode = VfxBlendMode.Additive,
				BaseColor = "#ff7711",
				SecondaryColor = "#aa1100"
			};

			Hud?.OpenVfxStudioDialog(newCfg, (savedCfg) =>
			{
				RefreshVfxList();
				SelectVfx(savedCfg.VfxId);
				_onSelectedCallback?.Invoke(savedCfg);
			});
		}, "Create a new custom VFX with VFX Studio", 11, new Vector2(100, 26));
		btnNewVfx.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);

		BodyContainer.AddChild(toolbarRow);

		_vfxListContainer = CreateScrollBody(260);
	}

	public void Open(Action<VfxAttachmentConfig>? onSelected = null, string? initialVfxId = null)
	{
		_onSelectedCallback = onSelected;
		_selectedVfxId = initialVfxId ?? string.Empty;
		if (_txtSearchFilter != null) _txtSearchFilter.Text = string.Empty;

		RefreshVfxList();
		ResetCameraDefault();
		OpenDialog();
	}

	public void RefreshVfxList()
	{
		var configs = GetAllAvailableVfxConfigs();
		if (string.IsNullOrEmpty(_selectedVfxId) && configs.Count > 0)
		{
			_selectedVfxId = configs.Keys.First();
		}

		if (!string.IsNullOrEmpty(_selectedVfxId) && configs.TryGetValue(_selectedVfxId, out var cfg))
		{
			_selectedConfig = cfg;
		}
		else if (configs.Count > 0)
		{
			_selectedVfxId = configs.Keys.First();
			_selectedConfig = configs[_selectedVfxId];
		}
		else
		{
			_selectedConfig = null;
		}

		UpdateSelectedVfxPreview();
		RebuildVfxListUI();
	}

	private void SelectVfx(string vfxId)
	{
		_selectedVfxId = vfxId;
		var configs = GetAllAvailableVfxConfigs();
		if (configs.TryGetValue(vfxId, out var cfg))
		{
			_selectedConfig = cfg;
		}
		else
		{
			_selectedConfig = null;
		}

		UpdateSelectedVfxPreview();
		RebuildVfxListUI();
		_onSelectedCallback?.Invoke(_selectedConfig ?? new VfxAttachmentConfig { VfxId = vfxId, Name = vfxId });
	}

	private void UpdateSelectedVfxPreview()
	{
		if (_lblSelectedVfxId != null) _lblSelectedVfxId.Text = !string.IsNullOrEmpty(_selectedVfxId) ? _selectedVfxId : "-";
		if (_lblSelectedVfxName != null) _lblSelectedVfxName.Text = _selectedConfig != null && !string.IsNullOrEmpty(_selectedConfig.Name) ? _selectedConfig.Name : "-";

		if (_previewVfxInstance != null && GodotObject.IsInstanceValid(_previewVfxInstance))
		{
			_previewVfxInstance.QueueFree();
			_previewVfxInstance = null;
		}

		if (_selectedConfig != null && PreviewSceneRoot != null)
		{
			_previewVfxInstance = new ProceduralVfxInstance3D(_selectedConfig);
			_previewVfxInstance.IsPreview = true;
			PreviewSceneRoot.AddChild(_previewVfxInstance);
		}
	}

	private void RebuildVfxListUI()
	{
		if (_vfxListContainer == null) return;

		foreach (Node child in _vfxListContainer.GetChildren())
		{
			child.QueueFree();
		}

		var configs = GetAllAvailableVfxConfigs();
		string filter = _txtSearchFilter != null ? _txtSearchFilter.Text.Trim() : string.Empty;

		var filteredEntries = configs.Values.Where(c =>
		{
			if (string.IsNullOrEmpty(filter)) return true;
			return c.VfxId.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
				   c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
				   c.PrimitiveType.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);
		}).OrderBy(c => c.VfxId).ToList();

		if (filteredEntries.Count == 0)
		{
			var emptyLabel = new Label
			{
				Text = TranslationServer.Translate("No VFX configurations found. Click '+ New VFX' to create one."),
				AutowrapMode = TextServer.AutowrapMode.Word
			};
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			emptyLabel.AddThemeFontSizeOverride("font_size", 11);
			_vfxListContainer.AddChild(emptyLabel);
			return;
		}

		for (int i = 0; i < filteredEntries.Count; i++)
		{
			var entry = filteredEntries[i];
			bool isSelected = string.Equals(entry.VfxId, _selectedVfxId, StringComparison.OrdinalIgnoreCase);

			var card = new PanelContainer();
			card.AddThemeStyleboxOverride("panel", isSelected ? UIStyle.CreateActiveInnerPanel() : UIStyle.CreateLightInnerPanel());

			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);

			var badge = new Label
			{
				Text = entry.PrimitiveType == VfxPrimitiveType.ParticleSystem ? "[Particle]" : $"[{entry.PrimitiveType}]",
				CustomMinimumSize = new Vector2(100, 0),
				ClipText = true
			};
			badge.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			badge.AddThemeFontSizeOverride("font_size", 10);
			row.AddChild(badge);

			var nameLbl = new Label
			{
				Text = $"✨ {entry.VfxId} ({entry.Name})",
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				ClipText = true
			};
			nameLbl.AddThemeColorOverride("font_color", isSelected ? UIStyle.ColorCyanGlow : UIStyle.ColorGold);
			nameLbl.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(nameLbl);

			string capturedVfxId = entry.VfxId;
			var capturedCfg = entry;

			var btnPreview = new Button();
			btnPreview.Set("icon_max_width", 0);
			btnPreview.Text = "👁️ " + TranslationServer.Translate("Preview");
			btnPreview.AddThemeFontSizeOverride("font_size", 10);
			btnPreview.CustomMinimumSize = new Vector2(65, 22);
			btnPreview.FocusMode = Control.FocusModeEnum.None;
			btnPreview.TooltipText = TranslationServer.Translate("Preview this VFX in 3D");
			btnPreview.Pressed += () => SelectVfx(capturedVfxId);
			row.AddChild(btnPreview);

			var btnEdit = new Button();
			btnEdit.Set("icon_max_width", 0);
			btnEdit.Text = "✏️ " + TranslationServer.Translate("Edit");
			btnEdit.AddThemeFontSizeOverride("font_size", 10);
			btnEdit.CustomMinimumSize = new Vector2(55, 22);
			btnEdit.FocusMode = Control.FocusModeEnum.None;
			btnEdit.TooltipText = TranslationServer.Translate("Open and edit this VFX in VFX Studio");
			btnEdit.Pressed += () =>
			{
				Hud?.OpenVfxStudioDialog(capturedCfg, (updatedCfg) =>
				{
					RefreshVfxList();
					SelectVfx(updatedCfg.VfxId);
					_onSelectedCallback?.Invoke(updatedCfg);
				});
			};
			row.AddChild(btnEdit);

			var btnDelete = new Button();
			btnDelete.Set("icon_max_width", 0);
			btnDelete.Text = "🗑️";
			btnDelete.AddThemeFontSizeOverride("font_size", 10);
			btnDelete.AddThemeColorOverride("font_color", new Color(1.0f, 0.45f, 0.45f));
			btnDelete.CustomMinimumSize = new Vector2(28, 22);
			btnDelete.FocusMode = Control.FocusModeEnum.None;
			btnDelete.TooltipText = TranslationServer.Translate("Delete this VFX");
			btnDelete.Pressed += () =>
			{
				Hud?.RemoveCustomVfxFromMetadata(capturedVfxId);
				RefreshVfxList();
			};
			row.AddChild(btnDelete);

			card.AddChild(row);
			_vfxListContainer.AddChild(card);
		}
	}

	public static Dictionary<string, VfxAttachmentConfig> GetAllAvailableVfxConfigs()
	{
		var result = new Dictionary<string, VfxAttachmentConfig>(StringComparer.OrdinalIgnoreCase);

		if (GameHost.VfxRegistry != null)
		{
			foreach (var kvp in GameHost.VfxRegistry)
			{
				if (kvp.Value != null)
				{
					result[kvp.Key] = kvp.Value.Clone();
				}
			}
		}

		string wsPath = !string.IsNullOrEmpty(MapWorkspaceService.GetActiveWorkspacePath())
			? MapWorkspaceService.GetActiveWorkspacePath()
			: ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		try
		{
			var metadata = Realm.Shared.Services.MapFileService.LoadMetadata(wsPath);
			if (metadata?.Templates?.Vfx != null)
			{
				foreach (var parsed in metadata.Templates.Vfx)
				{
					if (parsed != null && !string.IsNullOrEmpty(parsed.VfxId) && !result.ContainsKey(parsed.VfxId))
					{
						result[parsed.VfxId] = parsed;
					}
				}
			}
		}
		catch { }

		try
		{
			var metadata = MetadataService.Instance.LoadMetadata(wsPath);
			if (metadata.Templates?.Vfx != null)
			{
				foreach (var cfg in metadata.Templates.Vfx)
				{
					if (cfg != null && !string.IsNullOrEmpty(cfg.VfxId))
					{
						result[cfg.VfxId] = cfg;
					}
				}
			}
		}
		catch { }

		return result;
	}

	public override void CloseDialog()
	{
		if (_previewVfxInstance != null && GodotObject.IsInstanceValid(_previewVfxInstance))
		{
			_previewVfxInstance.QueueFree();
			_previewVfxInstance = null;
		}
		base.CloseDialog();
	}
}
