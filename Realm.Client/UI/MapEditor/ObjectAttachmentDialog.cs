using Godot;
using Realm.Client.Animation;
using Realm.Client.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Realm.Client.UI.MapEditor;

public partial class ObjectAttachmentDialog : Realm.Client.UI.MapEditor.FloatingPreview3DDialogBase
{
	private Node3D _previewModel;

	private readonly Dictionary<string, Node3D> _socketAnchorNodes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Node3D> _socketModelNodes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Node3D> _activeAttachmentVisuals = new(StringComparer.OrdinalIgnoreCase);
	private Node3D _currentActiveModelNode;
	private Node3D _uncommittedPreviewNode;
	private string _uncommittedPreviewKey;
	private VBoxContainer _configuredAttachmentsContainer;

	private Label _lblUnitTitle;
	private Label _lblUnitValue;
	private OptionButton _optSocket;
	private OptionButton _optAttachmentPicker;
	private OptionButton _optParent;
	private string? _currentParentAttachmentId = null;
	private readonly List<string?> _availableParents = new();

	private HSlider _sliderPosX;
	private HSlider _sliderPosY;
	private HSlider _sliderPosZ;
	private Label _lblPosX;
	private Label _lblPosY;
	private Label _lblPosZ;

	private HSlider _sliderRotX;
	private HSlider _sliderRotY;
	private HSlider _sliderRotZ;
	private Label _lblRotX;
	private Label _lblRotY;
	private Label _lblRotZ;

	private HSlider _sliderScaleX;
	private HSlider _sliderScaleY;
	private HSlider _sliderScaleZ;
	private Label _lblScaleX;
	private Label _lblScaleY;
	private Label _lblScaleZ;

	private HSlider _sliderNormalOffset;
	private Label _lblNormalOffset;

	private bool _isTargetBuilding = false;
	private string _targetObjectId = string.Empty;
	private string _currentAttachmentId = string.Empty;
	private string _currentSocketId = "RightHand";

	private Vector3 _currentPosOffset = Vector3.Zero;
	private Vector3 _currentRotOffset = Vector3.Zero;
	private Vector3 _currentScaleOffset = Vector3.One;
	private float _currentNormalOffset = 0.0f;

	private List<string> _availableAttachments = new();
	private bool _isUpdatingUI;

	private Action<HandAttachmentOrientation> _onApplied;
	private UnitObjectAttachments? _initialSnapshot;
	private UnitObjectAttachments _workingAttachments;

	public struct SocketDefinition
	{
		public string SocketId;
		public string DisplayName;
		public bool IsPseudoSocket;
		public HumanoidBone? AssociatedBone;

		public SocketDefinition(string socketId, string displayName, bool isPseudo, HumanoidBone? bone = null)
		{
			SocketId = socketId;
			DisplayName = displayName;
			IsPseudoSocket = isPseudo;
			AssociatedBone = bone;
		}
	}

	private static readonly SocketDefinition[] RiggedSockets = new[]
	{
		new SocketDefinition("Ground", "Ground / Footprint (Aura Ring)", true),
		new SocketDefinition("Center", "Center of Mass (Shield / Sphere)", true),
		new SocketDefinition("Overhead", "Overhead (Crown / Status Icon)", true),
		new SocketDefinition("RightHand", "Right Hand (Weapon)", false, HumanoidBone.RightHand),
		new SocketDefinition("LeftHand", "Left Hand (Offhand/Shield)", false, HumanoidBone.LeftHand),
		new SocketDefinition("Chest", "Chest (Torso Bone)", false, HumanoidBone.Chest),
		new SocketDefinition("Hips", "Hips (Pelvis Bone)", false, HumanoidBone.Hips),
		new SocketDefinition("Head", "Head (Head Bone)", false, HumanoidBone.Head),
		new SocketDefinition("LeftFoot", "Left Foot", false, HumanoidBone.LeftFoot),
		new SocketDefinition("RightFoot", "Right Foot", false, HumanoidBone.RightFoot)
	};

	private static readonly SocketDefinition[] NonRiggedSockets = new[]
	{
		new SocketDefinition("Center", "Model Center (Portal / Core)", true),
		new SocketDefinition("Top", "Model Top / Roof", true),
		new SocketDefinition("Base", "Model Base / Ground", true),
		new SocketDefinition("Pivot", "Root / Pivot (0, 0, 0)", true)
	};

	private readonly List<SocketDefinition> _currentAvailableSockets = new();

	public ObjectAttachmentDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Socket & VFX Attachment Studio"), new Vector2(580, 720))
	{
		DefaultDistance = 3.5f;
		CameraDistance = 3.5f;
		DefaultYaw = 0f;
		DefaultPitch = 0.2f;
		CameraYaw = 0f;
		CameraPitch = 0.2f;
		DefaultTargetPosition = new Vector3(0f, 1.0f, 0f);
		TargetPosition = DefaultTargetPosition;

		BuildControls();
	}

	private void BuildControls()
	{
		Add3DPreviewViewport(BodyContainer, new Vector2(530, 230));

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		AddCameraPresetToolbar(topControlsVBox, includeBack: true);
		BuildTopInfoRow(topControlsVBox);
		BuildSocketRow(topControlsVBox);
		BuildAttachmentPickerRow(topControlsVBox);
		BuildTransformSliders();

		AddSectionHeader(BodyContainer, "📎 " + TranslationServer.Translate("CONFIGURED ATTACHMENTS & VFX"), new Color(0.85f, 0.75f, 0.4f));
		_configuredAttachmentsContainer = CreateScrollBody(150);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("APPLY");
	}

	private void BuildTopInfoRow(Container parent)
	{
		var infoRow = new HBoxContainer();
		infoRow.AddThemeConstantOverride("separation", 8);

		_lblUnitTitle = new Label { Text = TranslationServer.Translate("Unit:") };
		_lblUnitTitle.AddThemeFontSizeOverride("font_size", 11);
		_lblUnitTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		infoRow.AddChild(_lblUnitTitle);

		_lblUnitValue = new Label { Text = "-" };
		_lblUnitValue.AddThemeFontSizeOverride("font_size", 11);
		_lblUnitValue.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		infoRow.AddChild(_lblUnitValue);

		var infoSpacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		infoRow.AddChild(infoSpacer);

		AddButton(infoRow, $"{UnicodeIcons.COPY} " + TranslationServer.Translate("Copy Code"), () =>
		{
			string code = $"api.SetUnitHandAttachment(unit, \"{_currentSocketId}\", \"{_currentAttachmentId}\");";
			DisplayServer.ClipboardSet(code);
			Hud?.ShowFeedback(TranslationServer.Translate("Copied script code to clipboard"));
		}, "Copy C# MapScript code reference to clipboard", 10, new Vector2(0, 20));

		parent.AddChild(infoRow);
	}

	private void BuildSocketRow(Container parent)
	{
		var socketRow = new HBoxContainer();
		socketRow.AddThemeConstantOverride("separation", 6);

		var lblSocketTitle = new Label { Text = TranslationServer.Translate("Socket:") };
		lblSocketTitle.AddThemeFontSizeOverride("font_size", 11);
		lblSocketTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		socketRow.AddChild(lblSocketTitle);

		_optSocket = new OptionButton();
		_optSocket.AddThemeFontSizeOverride("font_size", 11);
		_optSocket.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optSocket.CustomMinimumSize = new Vector2(160, 24);
		_optSocket.ItemSelected += (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx >= 0 && idx < _currentAvailableSockets.Count)
			{
				_currentSocketId = _currentAvailableSockets[(int)idx].SocketId;
				_currentParentAttachmentId = null;
				UpdateParentDropdown();
				PreviewCurrentAttachment();
			}
		};
		socketRow.AddChild(_optSocket);

		var lblParentTitle = new Label { Text = TranslationServer.Translate("Attach To:") };
		lblParentTitle.AddThemeFontSizeOverride("font_size", 11);
		lblParentTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		socketRow.AddChild(lblParentTitle);

		_optParent = new OptionButton();
		_optParent.AddThemeFontSizeOverride("font_size", 11);
		_optParent.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optParent.CustomMinimumSize = new Vector2(160, 24);
		_optParent.ItemSelected += (idx) =>
		{
			if (_isUpdatingUI) return;
			string? parentId = null;
			if (idx > 0 && idx < _availableParents.Count)
			{
				parentId = _availableParents[(int)idx];
			}
			_currentParentAttachmentId = parentId;
			PreviewCurrentAttachment();
		};
		socketRow.AddChild(_optParent);

		parent.AddChild(socketRow);
	}

	private void BuildAttachmentPickerRow(Container parent)
	{
		var pickerRow = new HBoxContainer();
		pickerRow.AddThemeConstantOverride("separation", 6);

		var lblAttTitle = new Label { Text = TranslationServer.Translate("Attachment / VFX:") };
		lblAttTitle.AddThemeFontSizeOverride("font_size", 11);
		lblAttTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		pickerRow.AddChild(lblAttTitle);

		_optAttachmentPicker = new OptionButton();
		_optAttachmentPicker.AddThemeFontSizeOverride("font_size", 11);
		_optAttachmentPicker.CustomMinimumSize = new Vector2(220, 24);
		_optAttachmentPicker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optAttachmentPicker.ItemSelected += (idx) =>
		{
			if (_isUpdatingUI) return;
			if (idx >= 0 && idx < _availableAttachments.Count)
			{
				_currentAttachmentId = _availableAttachments[(int)idx];
				UpdateParentDropdown();
				PreviewCurrentAttachment();
			}
		};
		pickerRow.AddChild(_optAttachmentPicker);

		parent.AddChild(pickerRow);
	}

	private void BuildTransformSliders()
	{
		AddSectionHeader(BodyContainer, "🗡️ " + TranslationServer.Translate("TRANSFORM & NORMAL OFFSET"));

		var transformCols = new HBoxContainer();
		transformCols.AddThemeConstantOverride("separation", 12);

		var leftCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		leftCol.AddThemeConstantOverride("separation", 4);
		var rightCol = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		rightCol.AddThemeConstantOverride("separation", 4);

		var posResultX = AddSlider(leftCol, TranslationServer.Translate("Position X:"), -1.5f, 1.5f, 0.005f, 0f, (v) => { _currentPosOffset.X = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.000", 90f);
		_sliderPosX = posResultX.Slider; _lblPosX = posResultX.ValueLabel;

		var posResultY = AddSlider(leftCol, TranslationServer.Translate("Position Y:"), -1.5f, 1.5f, 0.005f, 0f, (v) => { _currentPosOffset.Y = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.000", 90f);
		_sliderPosY = posResultY.Slider; _lblPosY = posResultY.ValueLabel;

		var posResultZ = AddSlider(leftCol, TranslationServer.Translate("Position Z:"), -1.5f, 1.5f, 0.005f, 0f, (v) => { _currentPosOffset.Z = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.000", 90f);
		_sliderPosZ = posResultZ.Slider; _lblPosZ = posResultZ.ValueLabel;

		var scaleResultX = AddSlider(leftCol, TranslationServer.Translate("Scale X:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) => { _currentScaleOffset.X = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.00", 90f);
		_sliderScaleX = scaleResultX.Slider; _lblScaleX = scaleResultX.ValueLabel;

		var scaleResultY = AddSlider(leftCol, TranslationServer.Translate("Scale Y:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) => { _currentScaleOffset.Y = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.00", 90f);
		_sliderScaleY = scaleResultY.Slider; _lblScaleY = scaleResultY.ValueLabel;

		var scaleResultZ = AddSlider(leftCol, TranslationServer.Translate("Scale Z:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) => { _currentScaleOffset.Z = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.00", 90f);
		_sliderScaleZ = scaleResultZ.Slider; _lblScaleZ = scaleResultZ.ValueLabel;

		var rotResultX = AddSlider(rightCol, TranslationServer.Translate("Pitch X (deg):"), -180f, 180f, 1f, 0f, (v) => { _currentRotOffset.X = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0", 95f);
		_sliderRotX = rotResultX.Slider; _lblRotX = rotResultX.ValueLabel;

		var rotResultY = AddSlider(rightCol, TranslationServer.Translate("Yaw Y (deg):"), -180f, 180f, 1f, 0f, (v) => { _currentRotOffset.Y = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0", 95f);
		_sliderRotY = rotResultY.Slider; _lblRotY = rotResultY.ValueLabel;

		var rotResultZ = AddSlider(rightCol, TranslationServer.Translate("Roll Z (deg):"), -180f, 180f, 1f, 0f, (v) => { _currentRotOffset.Z = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0", 95f);
		_sliderRotZ = rotResultZ.Slider; _lblRotZ = rotResultZ.ValueLabel;

		var normalResult = AddSlider(rightCol, TranslationServer.Translate("Normal Offset:"), -0.5f, 0.5f, 0.005f, 0.0f, (v) => { _currentNormalOffset = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.000", 95f);
		_sliderNormalOffset = normalResult.Slider; _lblNormalOffset = normalResult.ValueLabel;

		var btnRow = new HBoxContainer();
		btnRow.AddThemeConstantOverride("separation", 8);
		AddButton(btnRow, "⚡ " + TranslationServer.Translate("Auto Calculate"), () => AutoCalculateAttachmentOrientation(), "Automatically calculate orientation and position based on attachment bounds and camera view", 11, new Vector2(130, 24));
		AddButton(btnRow, "⟲ " + TranslationServer.Translate("Reset"), () => ResetTransformValues(), "Reset transform to defaults", 11, new Vector2(80, 24));
		var btnAdd = AddButton(btnRow, "➕ " + TranslationServer.Translate("Add"), () => AddOrUpdateCurrentAttachment(), "Add or update this attachment on the selected socket", 11, new Vector2(80, 24));
		btnAdd.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		rightCol.AddChild(btnRow);

		transformCols.AddChild(leftCol);
		transformCols.AddChild(rightCol);
		BodyContainer.AddChild(transformCols);
	}

	public void OpenForUnitAndAttachment(
		string unitId,
		string attachmentId = null,
		string socket = null,
		Node3D sourceModel = null,
		Action<HandAttachmentOrientation> onApplied = null)
	{
		OpenForTarget(unitId, attachmentId, socket, sourceModel, onApplied);
	}

	public void OpenForTarget(
		string targetId,
		string attachmentId = null,
		string socket = null,
		Node3D sourceModel = null,
		Action<HandAttachmentOrientation> onApplied = null)
	{
		_onApplied = onApplied;

		SetTargetObject(targetId);

		if (Realm.Client.Core.GameHost.TryGetUnitOrBuildingMetadata(_targetObjectId, out var meta))
		{
			_initialSnapshot = meta.ObjectAttachments?.Clone();
		}
		else
		{
			_initialSnapshot = null;
		}

		_workingAttachments = _initialSnapshot?.Clone() ?? new UnitObjectAttachments();

		string defaultSocket = _isTargetBuilding ? "Center" : "RightHand";
		_currentSocketId = NormalizeSocketId(string.IsNullOrEmpty(socket) ? defaultSocket : socket);
		_currentAttachmentId = attachmentId ?? string.Empty;

		UpdateTitles();

		RefreshAttachmentList();
		LoadTargetModelAndRig(_targetObjectId, sourceModel);
		LoadAttachmentOrientationIntoSliders(_targetObjectId, _currentSocketId, _currentAttachmentId, _currentParentAttachmentId);
		UpdateParentDropdown();
		RebuildConfiguredAttachmentsUI();

		ResetCameraDefault();
		OpenDialog();
	}

	private void SetTargetObject(string targetId)
	{
		if (string.IsNullOrEmpty(targetId))
		{
			if (Realm.Client.Core.GameHost.UnitRegistry != null && Realm.Client.Core.GameHost.UnitRegistry.Count > 0)
			{
				targetId = Realm.Client.Core.GameHost.UnitRegistry.Keys.First();
			}
			else if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.Count > 0)
			{
				targetId = Realm.Client.Core.GameHost.BuildingRegistry.Keys.First();
			}
		}

		_targetObjectId = targetId ?? string.Empty;
		_isTargetBuilding = Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.ContainsKey(_targetObjectId);
	}

	private void UpdateTitles()
	{
		if (_isTargetBuilding)
		{
			TitleLabel.Text = TranslationServer.Translate("Building VFX & Attachment Studio");
			if (_lblUnitTitle != null) _lblUnitTitle.Text = TranslationServer.Translate("Building:");
		}
		else
		{
			TitleLabel.Text = TranslationServer.Translate("Socket & VFX Attachment Studio");
			if (_lblUnitTitle != null) _lblUnitTitle.Text = TranslationServer.Translate("Unit:");
		}

		if (_lblUnitValue != null)
		{
			_lblUnitValue.Text = !string.IsNullOrEmpty(_targetObjectId) ? _targetObjectId : "-";
		}
	}

	public static string NormalizeSocketId(string socket)
	{
		if (string.IsNullOrEmpty(socket)) return "RightHand";
		string s = socket.ToLowerInvariant().Replace("_", "").Replace(" ", "");
		
		return TryMatchExactSocket(s) ?? TryMatchContainsSocket(s) ?? "RightHand";
	}

	private static string? TryMatchExactSocket(string s)
	{
		if (s == "top" || s == "roof") return "Top";
		if (s == "base") return "Base";
		if (s == "pivot" || s == "origin") return "Pivot";
		return null;
	}

	private static readonly Dictionary<string, string[]> _socketMappings = new()
	{
		{ "Ground", new[] { "ground", "footprint" } },
		{ "Overhead", new[] { "overhead", "crown" } },
		{ "Chest", new[] { "chest" } },
		{ "Center", new[] { "center", "centerofmass", "middle" } },
		{ "LeftHand", new[] { "lefthand" } },
		{ "RightHand", new[] { "righthand" } },
		{ "Hips", new[] { "hips", "pelvis", "root" } },
		{ "Head", new[] { "head" } },
		{ "LeftFoot", new[] { "leftfoot" } },
		{ "RightFoot", new[] { "rightfoot" } }
	};

	private static string? TryMatchContainsSocket(string s)
	{
		foreach (var kvp in _socketMappings)
		{
			foreach (var match in kvp.Value)
			{
				if (s.Contains(match)) return kvp.Key;
			}
		}
		return null;
	}

	private void UpdateSocketDropdown()
	{
		if (_optSocket == null) return;
		_isUpdatingUI = true;
		_optSocket.Clear();
		int selectedIdx = 0;
		for (int i = 0; i < _currentAvailableSockets.Count; i++)
		{
			var sock = _currentAvailableSockets[i];
			_optSocket.AddItem(TranslationServer.Translate(sock.DisplayName), i);
			if (sock.SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase))
			{
				selectedIdx = i;
			}
		}
		_optSocket.Selected = selectedIdx;
		if (_currentAvailableSockets.Count > 0)
		{
			_currentSocketId = _currentAvailableSockets[selectedIdx].SocketId;
		}
		_isUpdatingUI = false;
		UpdateParentDropdown();
	}

	private void UpdateParentDropdown()
	{
		if (_optParent == null) return;
		bool prevUpdating = _isUpdatingUI;
		_isUpdatingUI = true;
		_optParent.Clear();
		_availableParents.Clear();

		_optParent.AddItem(string.Format(TranslationServer.Translate("Socket: {0} (Direct)"), _currentSocketId), 0);
		_availableParents.Add(null);

		var configured = GetConfiguredAttachments();
		string normSocket = NormalizeSocketId(_currentSocketId);
		int selectedIdx = 0;

		foreach (var entry in configured)
		{
			if (!ShouldIncludeAsParent(entry, normSocket)) continue;

			string cleanId = System.IO.Path.GetFileNameWithoutExtension(entry.AttachmentId);
			if (_availableParents.Contains(cleanId) || _availableParents.Contains(entry.AttachmentId)) continue;

			int itemIdx = _availableParents.Count;
			_availableParents.Add(entry.AttachmentId);
			
			AddParentOption(entry, cleanId, itemIdx);

			if (IsCurrentParent(entry.AttachmentId, cleanId))
			{
				selectedIdx = itemIdx;
			}
		}

		if (selectedIdx == 0)
		{
			_currentParentAttachmentId = null;
		}
		_optParent.Selected = selectedIdx;
		_isUpdatingUI = prevUpdating;
	}

	private bool ShouldIncludeAsParent(ConfiguredAttachmentEntry entry, string normSocket)
	{
		if (!entry.SocketId.Equals(normSocket, StringComparison.OrdinalIgnoreCase)) return false;
		if (string.Equals(entry.AttachmentId, _currentAttachmentId, StringComparison.OrdinalIgnoreCase)) return false;
		if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId)) return false;
		return true;
	}

	private void AddParentOption(ConfiguredAttachmentEntry entry, string cleanId, int itemIdx)
	{
		string displayName = entry.AttachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? $"✨ {entry.AttachmentId.Substring(4)}"
			: $"🗡️ {cleanId}";
		_optParent.AddItem(string.Format(TranslationServer.Translate("Mesh: {0}"), displayName), itemIdx);
	}

	private bool IsCurrentParent(string attachmentId, string cleanId)
	{
		if (string.IsNullOrEmpty(_currentParentAttachmentId)) return false;
		return string.Equals(_currentParentAttachmentId, attachmentId, StringComparison.OrdinalIgnoreCase) ||
		       string.Equals(_currentParentAttachmentId, cleanId, StringComparison.OrdinalIgnoreCase);
	}

	private void RefreshAttachmentList()
	{
		_availableAttachments = GetAvailableObjectAttachmentIds();
		if (_optAttachmentPicker == null) return;

		_isUpdatingUI = true;
		_optAttachmentPicker.Clear();
		
		int selectedIdx = 0;
		string cleanCurrent = GetCleanAttachmentId(_currentAttachmentId);

		for (int i = 0; i < _availableAttachments.Count; i++)
		{
			string item = _availableAttachments[i];
			AddAttachmentOption(item, i);

			if (!string.IsNullOrEmpty(cleanCurrent) && IsCurrentAttachmentMatch(item, cleanCurrent))
			{
				selectedIdx = i;
			}
		}

		if (_availableAttachments.Count > 0)
		{
			_optAttachmentPicker.Selected = selectedIdx;
			_currentAttachmentId = _availableAttachments[selectedIdx];
		}
		_isUpdatingUI = false;
	}

	private string GetCleanAttachmentId(string id)
	{
		if (string.IsNullOrEmpty(id)) return "";
		return id.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? id
			: System.IO.Path.GetFileNameWithoutExtension(id);
	}

	private void AddAttachmentOption(string item, int index)
	{
		string display = item.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? $"✨ {item.Substring(4)}"
			: $"🗡️ {item}";
		_optAttachmentPicker.AddItem(display, index);
	}

	private bool IsCurrentAttachmentMatch(string item, string cleanCurrent)
	{
		string cleanItem = GetCleanAttachmentId(item);
		return item.Equals(_currentAttachmentId, StringComparison.OrdinalIgnoreCase) ||
		       cleanItem.Equals(cleanCurrent, StringComparison.OrdinalIgnoreCase);
	}

	private void LoadAttachmentOrientationIntoSliders(string targetId, string socketId, string attachmentId, string? parentAttachmentId = null)
	{
		HandAttachmentOrientation? unitOrient = TryLoadConfiguredOrientation(targetId, socketId, attachmentId, parentAttachmentId);

		if (unitOrient != null)
		{
			ApplyConfiguredOrientation(unitOrient);
		}
		else if (!string.IsNullOrEmpty(attachmentId) && Realm.Client.Core.GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var attMeta))
		{
			ApplyRegistryOrientation(attMeta);
		}
		else
		{
			ApplyDefaultOrientation();
		}

		UpdateSliderDisplayValues();
	}

	private HandAttachmentOrientation? TryLoadConfiguredOrientation(string targetId, string socketId, string attachmentId, string? parentAttachmentId)
	{
		if (string.IsNullOrEmpty(targetId)) return null;

		var configured = GetConfiguredAttachments();
		string normSocket = NormalizeSocketId(socketId);
		string cleanId = GetCleanAttachmentId(attachmentId);

		foreach (var entry in configured)
		{
			if (IsOrientationMatch(entry, normSocket, attachmentId, cleanId, parentAttachmentId))
			{
				return entry.Orientation;
			}
		}
		return null;
	}

	private bool IsOrientationMatch(ConfiguredAttachmentEntry entry, string normSocket, string attachmentId, string cleanId, string? parentAttachmentId)
	{
		if (!entry.SocketId.Equals(normSocket, StringComparison.OrdinalIgnoreCase)) return false;

		bool keyMatch = entry.AttachmentId.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
		                entry.AttachmentId.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
		                System.IO.Path.GetFileNameWithoutExtension(entry.AttachmentId).Equals(cleanId, StringComparison.OrdinalIgnoreCase);

		if (!keyMatch) return false;

		return parentAttachmentId == null || string.Equals(entry.Orientation.ParentAttachmentId ?? string.Empty, parentAttachmentId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	private void ApplyConfiguredOrientation(HandAttachmentOrientation unitOrient)
	{
		_currentPosOffset = unitOrient.Position.ToGodotVector3();
		_currentRotOffset = unitOrient.RotationDegrees.ToGodotVector3();
		_currentScaleOffset = unitOrient.ScaleVector.ToGodotVector3();
		_currentNormalOffset = unitOrient.NormalOffset;
		_currentParentAttachmentId = unitOrient.ParentAttachmentId;
	}

	private void ApplyRegistryOrientation(AttachmentMetadata attMeta)
	{
		_currentPosOffset = attMeta.PositionOffset.ToGodotVector3();
		_currentRotOffset = attMeta.RotationOffset.ToGodotVector3();
		_currentScaleOffset = Vector3.One * (attMeta.Scale <= 0f ? 1.0f : attMeta.Scale);
		_currentNormalOffset = 0.0f;
	}

	private void ApplyDefaultOrientation()
	{
		_currentPosOffset = Vector3.Zero;
		_currentRotOffset = Vector3.Zero;
		_currentScaleOffset = Vector3.One;
		_currentNormalOffset = 0.0f;
	}

	private void UpdateSliderDisplayValues()
	{
		UpdateSlider(_sliderPosX, _lblPosX, _currentPosOffset.X, "F3");
		UpdateSlider(_sliderPosY, _lblPosY, _currentPosOffset.Y, "F3");
		UpdateSlider(_sliderPosZ, _lblPosZ, _currentPosOffset.Z, "F3");

		UpdateSlider(_sliderRotX, _lblRotX, _currentRotOffset.X, "F0");
		UpdateSlider(_sliderRotY, _lblRotY, _currentRotOffset.Y, "F0");
		UpdateSlider(_sliderRotZ, _lblRotZ, _currentRotOffset.Z, "F0");

		UpdateSlider(_sliderScaleX, _lblScaleX, _currentScaleOffset.X, "F2");
		UpdateSlider(_sliderScaleY, _lblScaleY, _currentScaleOffset.Y, "F2");
		UpdateSlider(_sliderScaleZ, _lblScaleZ, _currentScaleOffset.Z, "F2");

		UpdateSlider(_sliderNormalOffset, _lblNormalOffset, _currentNormalOffset, "F3");
	}

	private void UpdateSlider(HSlider? slider, Label? label, float value, string format)
	{
		if (slider != null) slider.Value = value;
		if (label != null) label.Text = value.ToString(format);
	}

	private void SyncWorkingAttachmentIfConfigured()
	{
		if (string.IsNullOrEmpty(_targetObjectId) || string.IsNullOrEmpty(_currentAttachmentId)) return;
		if (IsAttachmentConfigured(_currentSocketId, _currentAttachmentId, _currentParentAttachmentId))
		{
			var orientation = new HandAttachmentOrientation
			{
				PositionX = _currentPosOffset.X,
				PositionY = _currentPosOffset.Y,
				PositionZ = _currentPosOffset.Z,
				PitchX = _currentRotOffset.X,
				YawY = _currentRotOffset.Y,
				RollZ = _currentRotOffset.Z,
				Scale = _currentScaleOffset.X,
				ScaleX = _currentScaleOffset.X,
				ScaleY = _currentScaleOffset.Y,
				ScaleZ = _currentScaleOffset.Z,
				NormalOffset = _currentNormalOffset,
				ParentAttachmentId = _currentParentAttachmentId
			};
			_workingAttachments.SetSocketOrientation(_currentSocketId, _currentAttachmentId, orientation);
		}
	}

	private void ResetTransformValues()
	{
		_currentPosOffset = Vector3.Zero;
		_currentRotOffset = Vector3.Zero;
		_currentScaleOffset = Vector3.One;
		_currentNormalOffset = 0.0f;
		UpdateSliderDisplayValues();
		UpdateActiveAttachmentTransform();
		SyncWorkingAttachmentIfConfigured();
	}

	private void AutoCalculateAttachmentOrientation()
	{
		if (!string.IsNullOrEmpty(_currentParentAttachmentId))
		{
			ResetTransformValues();
			return;
		}

		_socketAnchorNodes.TryGetValue(_currentSocketId, out var targetAnchor);
		if (targetAnchor == null || !GodotObject.IsInstanceValid(targetAnchor)) return;

		Node3D targetNode = GetActiveTargetNode();
		if (targetNode == null) return;

		var currentDef = _currentAvailableSockets.FirstOrDefault(s => s.SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase));
		if (currentDef.IsPseudoSocket)
		{
			ApplyPseudoSocketOrientation();
			return;
		}

		CalculateAndApplyTransform(targetAnchor, targetNode, currentDef);
	}

	private Node3D GetActiveTargetNode()
	{
		Node3D targetNode = _currentActiveModelNode;
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
		{
			PreviewCurrentAttachment();
			targetNode = _currentActiveModelNode;
		}
		return (targetNode != null && GodotObject.IsInstanceValid(targetNode)) ? targetNode : null;
	}

	private void ApplyPseudoSocketOrientation()
	{
		bool isGround = _currentSocketId.Equals("Ground", StringComparison.OrdinalIgnoreCase) || 
		                _currentSocketId.Equals("Base", StringComparison.OrdinalIgnoreCase);
		
		_currentPosOffset = isGround ? new Vector3(0f, 0.02f, 0f) : Vector3.Zero;
		_currentRotOffset = Vector3.Zero;
		_currentNormalOffset = 0.0f;
		
		UpdateSliderDisplayValues();
		UpdateActiveAttachmentTransform();
		SyncWorkingAttachmentIfConfigured();
	}

	private void CalculateAndApplyTransform(Node3D targetAnchor, Node3D targetNode, SocketDefinition currentDef)
	{
		Transform3D savedTransform = targetNode.Transform;
		targetNode.Transform = Transform3D.Identity;

		Aabb localAabb = CalculateAttachmentLocalAabb(targetNode);
		targetNode.Transform = savedTransform;

		Vector3 effectiveScale = _currentScaleOffset == Vector3.Zero ? Vector3.One : _currentScaleOffset;
		int primaryAxis = GetPrimaryAxis(localAabb.Size, effectiveScale);
		Vector3 localGripPoint = CalculateGripPoint(localAabb, primaryAxis);

		Basis desiredLocalBasis = CalculateDesiredBasis(targetAnchor, currentDef, primaryAxis);
		ApplyCalculatedTransform(desiredLocalBasis, localGripPoint, effectiveScale);
	}

	private int GetPrimaryAxis(Vector3 size, Vector3 effectiveScale)
	{
		Vector3 scaledSize = new Vector3(size.X * Mathf.Abs(effectiveScale.X), size.Y * Mathf.Abs(effectiveScale.Y), size.Z * Mathf.Abs(effectiveScale.Z));
		if (scaledSize.X > scaledSize.Y && scaledSize.X > scaledSize.Z) return 0;
		if (scaledSize.Z > scaledSize.Y && scaledSize.Z > scaledSize.X) return 2;
		return 1;
	}

	private Vector3 CalculateGripPoint(Aabb localAabb, int primaryAxis)
	{
		Vector3 size = localAabb.Size;
		if (primaryAxis == 0)
		{
			float gripX = localAabb.Position.X < -0.05f && localAabb.End.X > 0.05f
				? localAabb.Position.X + size.X * 0.15f
				: localAabb.Position.X;
			return new Vector3(gripX, localAabb.GetCenter().Y, localAabb.GetCenter().Z);
		}
		if (primaryAxis == 2)
		{
			float gripZ = localAabb.Position.Z < -0.05f && localAabb.End.Z > 0.05f
				? localAabb.Position.Z + size.Z * 0.15f
				: localAabb.Position.Z;
			return new Vector3(localAabb.GetCenter().X, localAabb.GetCenter().Y, gripZ);
		}
		
		float gripY = localAabb.Position.Y < -0.05f && localAabb.End.Y > 0.05f
			? localAabb.Position.Y + size.Y * 0.15f
			: localAabb.Position.Y;
		return new Vector3(localAabb.GetCenter().X, gripY, localAabb.GetCenter().Z);
	}

	private Basis CalculateDesiredBasis(Node3D targetAnchor, SocketDefinition currentDef, int primaryAxis)
	{
		Vector3 handPos = targetAnchor.GlobalPosition;
		Vector3 camPos = PreviewCamera != null ? PreviewCamera.GlobalPosition : new Vector3(0f, 1.5f, 3f);
		Vector3 dirToCam = (camPos - handPos).Normalized();

		Vector3 wristNormal = CalculateWristNormal(targetAnchor, currentDef, handPos);

		Vector3 desiredTopDir = (dirToCam * 0.7f + Vector3.Up * 0.5f + wristNormal * 0.3f).Normalized();
		Vector3 desiredRight = desiredTopDir.Cross(Vector3.Up).Normalized();
		
		if (desiredRight.LengthSquared() < 0.001f)
		{
			Vector3 camRight = PreviewCamera != null ? PreviewCamera.GlobalTransform.Basis.X.Normalized() : Vector3.Right;
			desiredRight = desiredTopDir.Cross(camRight).Normalized();
		}

		if (currentDef.AssociatedBone == HumanoidBone.LeftHand)
		{
			desiredRight = -desiredRight;
		}

		Vector3 desiredNormal = desiredRight.Cross(desiredTopDir).Normalized();

		Basis desiredWorldBasis = primaryAxis == 0 
			? new Basis(desiredTopDir, desiredNormal, desiredRight)
			: primaryAxis == 2 
				? new Basis(desiredRight, desiredNormal, desiredTopDir) 
				: new Basis(desiredRight, desiredTopDir, desiredNormal);

		Basis handBasis = targetAnchor.GlobalTransform.Basis.Orthonormalized();
		return Mathf.Abs(handBasis.Determinant()) < 0.0001f 
			? desiredWorldBasis.Orthonormalized() 
			: (handBasis.Inverse() * desiredWorldBasis).Orthonormalized();
	}

	private Vector3 CalculateWristNormal(Node3D targetAnchor, SocketDefinition currentDef, Vector3 handPos)
	{
		Vector3 wristNormal = targetAnchor.GlobalTransform.Basis.Y.Normalized();
		var skeleton = SkeletonValidator.FindSkeleton(_previewModel);
		if (skeleton != null && currentDef.AssociatedBone.HasValue)
		{
			int lowerArmIdx = skeleton.FindBoneInSkeleton(
				currentDef.AssociatedBone.Value == HumanoidBone.RightHand ? HumanoidBone.RightLowerArm : HumanoidBone.LeftLowerArm);
			if (lowerArmIdx >= 0)
			{
				Vector3 lowerArmPos = skeleton.GlobalTransform * skeleton.GetBoneGlobalPose(lowerArmIdx).Origin;
				wristNormal = (handPos - lowerArmPos).Normalized();
			}
		}
		return wristNormal;
	}

	private void ApplyCalculatedTransform(Basis desiredLocalBasis, Vector3 localGripPoint, Vector3 effectiveScale)
	{
		Vector3 rotEulerRad = desiredLocalBasis.GetEuler();
		_currentRotOffset = new Vector3(
			NormalizeAngle(Mathf.RadToDeg(rotEulerRad.X)),
			NormalizeAngle(Mathf.RadToDeg(rotEulerRad.Y)),
			NormalizeAngle(Mathf.RadToDeg(rotEulerRad.Z))
		);

		Vector3 scaledGripPoint = new Vector3(
			localGripPoint.X * effectiveScale.X,
			localGripPoint.Y * effectiveScale.Y,
			localGripPoint.Z * effectiveScale.Z
		);

		Vector3 localOffset = -(desiredLocalBasis * scaledGripPoint);
		_currentPosOffset = new Vector3(
			Mathf.Clamp(localOffset.X, -1.5f, 1.5f),
			Mathf.Clamp(localOffset.Y, -1.5f, 1.5f),
			Mathf.Clamp(localOffset.Z, -1.5f, 1.5f)
		);

		UpdateSliderDisplayValues();
		UpdateActiveAttachmentTransform();
		SyncWorkingAttachmentIfConfigured();
	}

	private static Aabb CalculateAttachmentLocalAabb(Node3D root)
	{
		Aabb combinedAabb = new Aabb();
		bool hasAabb = false;
		if (root == null)
		{
			return combinedAabb;
		}

		void Collect(Node current)
		{
			if (current is MeshInstance3D meshInst && meshInst.Mesh != null)
			{
				ExpandAabbWithMesh(meshInst, root, ref combinedAabb, ref hasAabb);
			}
			foreach (Node child in current.GetChildren())
			{
				Collect(child);
			}
		}

		Collect(root);
		if (!hasAabb)
		{
			combinedAabb = new Aabb(new Vector3(-0.1f, -0.5f, -0.1f), new Vector3(0.2f, 1.0f, 0.2f));
		}
		return combinedAabb;
	}

	private static void ExpandAabbWithMesh(MeshInstance3D meshInst, Node3D root, ref Aabb combinedAabb, ref bool hasAabb)
	{
		Transform3D relXform = Transform3D.Identity;
		Node? curr = meshInst;
		while (curr != null && curr != root)
		{
			if (curr is Node3D n3d)
			{
				relXform = n3d.Transform * relXform;
			}
			curr = curr.GetParent();
		}

		if (Mathf.Abs(relXform.Basis.Determinant()) <= 0.0001f) return;

		Aabb mAabb = meshInst.Mesh.GetAabb();
		Vector3 min = mAabb.Position;
		Vector3 max = mAabb.End;
		Vector3[] corners = new[]
		{
			new Vector3(min.X, min.Y, min.Z),
			new Vector3(min.X, min.Y, max.Z),
			new Vector3(min.X, max.Y, min.Z),
			new Vector3(min.X, max.Y, max.Z),
			new Vector3(max.X, min.Y, min.Z),
			new Vector3(max.X, min.Y, max.Z),
			new Vector3(max.X, max.Y, min.Z),
			new Vector3(max.X, max.Y, max.Z)
		};
		
		for (int i = 0; i < 8; i++)
		{
			Vector3 pt = relXform * corners[i];
			if (!hasAabb)
			{
				combinedAabb = new Aabb(pt, Vector3.Zero);
				hasAabb = true;
			}
			else
			{
				combinedAabb = combinedAabb.Expand(pt);
			}
		}
	}

	private static float NormalizeAngle(float angle)
	{
		while (angle > 180f) angle -= 360f;
		while (angle < -180f) angle += 360f;
		return angle;
	}

	private void ClearUncommittedPreview()
	{
		if (_uncommittedPreviewNode != null && GodotObject.IsInstanceValid(_uncommittedPreviewNode))
		{
			if (!string.IsNullOrEmpty(_uncommittedPreviewKey))
			{
				_activeAttachmentVisuals.Remove(_uncommittedPreviewKey);
				if (_socketModelNodes.TryGetValue(_currentSocketId, out var smn) && smn == _uncommittedPreviewNode)
				{
					_socketModelNodes.Remove(_currentSocketId);
				}
			}
			if (_currentActiveModelNode == _uncommittedPreviewNode)
			{
				_currentActiveModelNode = null;
			}
			_uncommittedPreviewNode.GetParent()?.RemoveChild(_uncommittedPreviewNode);
			_uncommittedPreviewNode.QueueFree();
		}
		_uncommittedPreviewNode = null;
		_uncommittedPreviewKey = null;
	}

	private void ClearPreviewModel()
	{
		ClearUncommittedPreview();
		if (_previewModel != null && GodotObject.IsInstanceValid(_previewModel))
		{
			_previewModel.QueueFree();
			_previewModel = null;
		}
		_socketAnchorNodes.Clear();
		_socketModelNodes.Clear();
		_activeAttachmentVisuals.Clear();
		_currentActiveModelNode = null;
		_uncommittedPreviewNode = null;
		_uncommittedPreviewKey = null;
	}

	private void LoadTargetModelAndRig(string targetId, Node3D sourceModel)
	{
		ClearPreviewModel();
		if (string.IsNullOrEmpty(targetId)) return;

		Node3D modelToInstantiate = GetModelToInstantiate(targetId, sourceModel);
		if (modelToInstantiate == null) return;

		RemoveAllExistingAttachments(modelToInstantiate);

		_previewModel = modelToInstantiate;
		_previewModel.Position = Vector3.Zero;
		_previewModel.Rotation = Vector3.Zero;
		_previewModel.Scale = Vector3.One;
		PreviewSceneRoot.AddChild(_previewModel);

		SetupSockets(sourceModel);
		FinalizeModelLoading();
	}

	private Node3D GetModelToInstantiate(string targetId, Node3D sourceModel)
	{
		if (sourceModel != null && GodotObject.IsInstanceValid(sourceModel))
		{
			return (Node3D)sourceModel.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
		}

		if (string.IsNullOrEmpty(targetId)) return null;

		string modelPath = GetModelPath(targetId);
		var cached = ModelCache.GetModel(modelPath);
		if (cached is Node3D n3d)
		{
			return (Node3D)n3d.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
		}

		return null;
	}

	private string GetModelPath(string targetId)
	{
		if (Realm.Client.Core.GameHost.UnitRegistry != null && Realm.Client.Core.GameHost.UnitRegistry.TryGetValue(targetId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
			return meta.ModelPath;
		
		if (Realm.Client.Core.GameHost.BuildingRegistry != null && Realm.Client.Core.GameHost.BuildingRegistry.TryGetValue(targetId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
			return bldMeta.ModelPath;

		if (Realm.Client.Core.GameHost.PropRegistry != null && (Realm.Client.Core.GameHost.PropRegistry.TryGetValue(targetId, out var pMeta) || Realm.Client.Core.GameHost.PropRegistry.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(targetId), out pMeta)))
			return pMeta.ModelPath;

		return targetId;
	}

	private void SetupSockets(Node3D sourceModel)
	{
		var skeleton = SkeletonValidator.FindSkeleton(_previewModel);
		Aabb modelAabb = CalculateAttachmentLocalAabb(_previewModel);
		_currentAvailableSockets.Clear();

		if (skeleton != null && !_isTargetBuilding)
		{
			SetupRiggedSockets(skeleton, sourceModel, modelAabb);
		}
		else
		{
			SetupNonRiggedSockets(modelAabb);
		}
	}

	private void SetupRiggedSockets(Skeleton3D skeleton, Node3D sourceModel, Aabb modelAabb)
	{
		CopySkeletonPoses(skeleton, sourceModel);
		_currentAvailableSockets.AddRange(RiggedSockets);

		foreach (var sock in _currentAvailableSockets)
		{
			if (sock.IsPseudoSocket)
			{
				CreatePseudoSocketAnchor(sock, modelAabb);
			}
			else if (sock.AssociatedBone.HasValue)
			{
				CreateBoneAttachmentAnchor(sock, skeleton);
			}
		}
	}

	private void CopySkeletonPoses(Skeleton3D skeleton, Node3D sourceModel)
	{
		if (sourceModel == null || !GodotObject.IsInstanceValid(sourceModel)) return;
		
		var srcSkeleton = SkeletonValidator.FindSkeleton(sourceModel);
		if (srcSkeleton == null) return;

		int count = Math.Min(srcSkeleton.GetBoneCount(), skeleton.GetBoneCount());
		for (int i = 0; i < count; i++)
		{
			skeleton.SetBonePosePosition(i, srcSkeleton.GetBonePosePosition(i));
			skeleton.SetBonePoseRotation(i, srcSkeleton.GetBonePoseRotation(i));
			skeleton.SetBonePoseScale(i, srcSkeleton.GetBonePoseScale(i));
		}
	}

	private void CreatePseudoSocketAnchor(SocketDefinition sock, Aabb modelAabb)
	{
		var anchor = new Node3D { Name = $"Anchor_{sock.SocketId}" };
		Vector3 anchorPos = sock.SocketId switch
		{
			"Ground" => new Vector3(0, modelAabb.Position.Y, 0),
			"Center" => new Vector3(0, modelAabb.GetCenter().Y, 0),
			"Overhead" => new Vector3(0, modelAabb.End.Y + 0.3f, 0),
			_ => Vector3.Zero
		};
		anchor.Position = anchorPos;
		_previewModel.AddChild(anchor);
		_socketAnchorNodes[sock.SocketId] = anchor;
	}

	private void CreateBoneAttachmentAnchor(SocketDefinition sock, Skeleton3D skeleton)
	{
		int boneIdx = skeleton.FindBoneInSkeleton(sock.AssociatedBone.Value);
		if (boneIdx >= 0)
		{
			var ba = new BoneAttachment3D
			{
				Name = $"BoneAttachment_{sock.AssociatedBone.Value}",
				BoneName = skeleton.GetBoneName(boneIdx),
				BoneIdx = boneIdx
			};
			skeleton.AddChild(ba);
			_socketAnchorNodes[sock.SocketId] = ba;
		}
	}

	private void SetupNonRiggedSockets(Aabb modelAabb)
	{
		_currentAvailableSockets.AddRange(NonRiggedSockets);

		foreach (var sock in _currentAvailableSockets)
		{
			var anchor = new Node3D { Name = $"Anchor_{sock.SocketId}" };
			Vector3 anchorPos = sock.SocketId switch
			{
				"Center" => modelAabb.GetCenter(),
				"Top" => new Vector3(modelAabb.GetCenter().X, modelAabb.End.Y, modelAabb.GetCenter().Z),
				"Base" => new Vector3(modelAabb.GetCenter().X, modelAabb.Position.Y, modelAabb.GetCenter().Z),
				"Pivot" => Vector3.Zero,
				_ => Vector3.Zero
			};
			anchor.Position = anchorPos;
			_previewModel.AddChild(anchor);
			_socketAnchorNodes[sock.SocketId] = anchor;
		}
	}

	private void FinalizeModelLoading()
	{
		UpdateSocketDropdown();
		AttachAllConfiguredAttachments();

		if (!string.IsNullOrEmpty(_currentAttachmentId))
		{
			PreviewCurrentAttachment();
			return;
		}

		var configured = GetConfiguredAttachments();
		if (configured.Count > 0)
		{
			SelectConfiguredAttachment(configured[0].SocketId, configured[0].AttachmentId, configured[0].Index);
		}
		else if (_availableAttachments.Count > 0)
		{
			_currentAttachmentId = _availableAttachments[0];
			PreviewCurrentAttachment();
		}
		else
		{
			_currentActiveModelNode = null;
		}
	}

	private static void RemoveAllExistingAttachments(Node node)
	{
		if (node == null) return;
		var toRemove = new List<Node>();
		void Collect(Node current)
		{
			if (current is BoneAttachment3D ||
			    current.Name.ToString().StartsWith("SocketAttachment_", StringComparison.OrdinalIgnoreCase) ||
			    current.Name.ToString().StartsWith("PseudoSocket_", StringComparison.OrdinalIgnoreCase))
			{
				toRemove.Add(current);
				return;
			}
			foreach (Node child in current.GetChildren())
			{
				Collect(child);
			}
		}
		Collect(node);
		foreach (var n in toRemove)
		{
			n.GetParent()?.RemoveChild(n);
			n.QueueFree();
		}
	}

	public struct ConfiguredAttachmentEntry
	{
		public int Index;
		public string SocketId;
		public string AttachmentId;
		public HandAttachmentOrientation Orientation;
	}

	public static string GetAttachmentKey(string socketId, string attachmentId, int index = -1, string? parentAttachmentId = null)
	{
		string cleanAtt = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
		string normSocket = NormalizeSocketId(socketId);
		string parentPart = string.IsNullOrEmpty(parentAttachmentId) ? "" : $"_p_{System.IO.Path.GetFileNameWithoutExtension(parentAttachmentId)}";
		return index >= 0
			? $"{normSocket}_{index}_{cleanAtt}{parentPart}"
			: $"{normSocket}_{cleanAtt}{parentPart}";
	}

	private List<ConfiguredAttachmentEntry> GetConfiguredAttachments()
	{
		bool isNonRigged = _isTargetBuilding || _currentAvailableSockets.Any(s => s.SocketId == "Top" || s.SocketId == "Base" || s.SocketId == "Pivot");
		return GetConfiguredAttachmentsFromData(_workingAttachments, isNonRigged);
	}

	public static List<ConfiguredAttachmentEntry> GetConfiguredAttachmentsForObject(string targetObjectId, bool isBuilding = false)
	{
		if (Realm.Client.Core.GameHost.TryGetUnitOrBuildingMetadata(targetObjectId, out var meta))
		{
			return GetConfiguredAttachmentsFromData(meta.ObjectAttachments, isBuilding);
		}
		return new List<ConfiguredAttachmentEntry>();
	}

	public static List<ConfiguredAttachmentEntry> GetConfiguredAttachmentsFromData(UnitObjectAttachments? attsNode, bool isBuilding = false)
	{
		var list = new List<ConfiguredAttachmentEntry>();
		if (attsNode == null) return list;

		if (isBuilding)
		{
			CollectBuildingAttachments(list, attsNode);
		}
		else
		{
			CollectUnitAttachments(list, attsNode);
		}

		return list;
	}

	private static void CollectBuildingAttachments(List<ConfiguredAttachmentEntry> list, UnitObjectAttachments atts)
	{
		CollectToEntryList(list, "Center", atts.center ?? atts.chest);
		CollectToEntryList(list, "Top", atts.overhead ?? atts.head);
		CollectToEntryList(list, "Base", atts.ground ?? atts.root);
		CollectToEntryList(list, "Pivot", atts.pivot ?? atts.right_hand);
	}

	private static void CollectUnitAttachments(List<ConfiguredAttachmentEntry> list, UnitObjectAttachments atts)
	{
		CollectToEntryList(list, "RightHand", atts.right_hand);
		CollectToEntryList(list, "LeftHand", atts.left_hand);
		CollectToEntryList(list, "Chest", atts.chest);
		CollectToEntryList(list, "Hips", atts.root);
		CollectToEntryList(list, "Head", atts.head);
		CollectToEntryList(list, "LeftFoot", atts.left_foot);
		CollectToEntryList(list, "RightFoot", atts.right_foot);
		CollectToEntryList(list, "Ground", atts.ground);
		CollectToEntryList(list, "Center", atts.center);
		CollectToEntryList(list, "Overhead", atts.overhead);
		CollectToEntryList(list, "Pivot", atts.pivot);
	}

	private static void CollectToEntryList(List<ConfiguredAttachmentEntry> list, string socket, List<Dictionary<string, HandAttachmentOrientation>>? sockList)
	{
		if (sockList == null) return;
		for (int i = 0; i < sockList.Count; i++)
		{
			var dict = sockList[i];
			if (dict == null) continue;
			foreach (var kvp in dict)
			{
				list.Add(new ConfiguredAttachmentEntry
				{
					Index = i,
					SocketId = NormalizeSocketId(socket),
					AttachmentId = kvp.Key,
					Orientation = kvp.Value
				});
			}
		}
	}

	private void ResetConfiguredVisualTransforms()
	{
		var configured = GetConfiguredAttachments();
		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			string key = GetAttachmentKey(entry.SocketId, entry.AttachmentId, entry.Index, entry.Orientation.ParentAttachmentId);
			if (_activeAttachmentVisuals.TryGetValue(key, out var visualNode) && GodotObject.IsInstanceValid(visualNode))
			{
				visualNode.Position = entry.Orientation.Position.ToGodotVector3() + (Vector3.Up * entry.Orientation.NormalOffset);
				visualNode.RotationDegrees = entry.Orientation.RotationDegrees.ToGodotVector3();
				var entryScale = entry.Orientation.ScaleVector.ToGodotVector3();
				visualNode.Scale = entryScale == Vector3.Zero ? Vector3.One : entryScale;
			}
		}
	}

	private bool IsAttachmentConfigured(string socketId, string attachmentId, string? parentAttachmentId = null)
	{
		if (string.IsNullOrEmpty(attachmentId)) return false;
		string normSocket = NormalizeSocketId(socketId);
		string cleanId = System.IO.Path.GetFileNameWithoutExtension(attachmentId);
		var configured = GetConfiguredAttachments();
		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			if (entry.SocketId.Equals(normSocket, StringComparison.OrdinalIgnoreCase))
			{
				if (string.Equals(entry.Orientation.ParentAttachmentId ?? string.Empty, parentAttachmentId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
				{
					if (entry.AttachmentId.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
					    entry.AttachmentId.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
					    System.IO.Path.GetFileNameWithoutExtension(entry.AttachmentId).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private void PreviewCurrentAttachment()
	{
		ClearUncommittedPreview();
		ResetConfiguredVisualTransforms();

		if (string.IsNullOrEmpty(_currentAttachmentId))
		{
			_currentActiveModelNode = null;
			return;
		}

		LoadAttachmentOrientationIntoSliders(_targetObjectId, _currentSocketId, _currentAttachmentId, _currentParentAttachmentId);

		string normSocket = NormalizeSocketId(_currentSocketId);
		string key = GetAttachmentKey(normSocket, _currentAttachmentId, -1, _currentParentAttachmentId);

		if (IsAttachmentConfigured(normSocket, _currentAttachmentId, _currentParentAttachmentId))
		{
			if (_activeAttachmentVisuals.TryGetValue(key, out var visualNode) && GodotObject.IsInstanceValid(visualNode))
			{
				_currentActiveModelNode = visualNode;
				_socketModelNodes[normSocket] = visualNode;
			}
			else
			{
				var loaded = AttachVisualToAnchor(normSocket, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset, -1, _currentParentAttachmentId);
				_currentActiveModelNode = loaded;
				if (loaded != null)
				{
					_socketModelNodes[normSocket] = loaded;
				}
			}
			_uncommittedPreviewNode = null;
			_uncommittedPreviewKey = null;
		}
		else
		{
			var loaded = AttachVisualToAnchor(normSocket, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset, -1, _currentParentAttachmentId);
			_currentActiveModelNode = loaded;
			_uncommittedPreviewNode = loaded;
			_uncommittedPreviewKey = key;
			if (loaded != null)
			{
				_socketModelNodes[normSocket] = loaded;
			}
		}

		UpdateActiveAttachmentTransform();
	}

	private void AttachAllConfiguredAttachments()
	{
		ClearUncommittedPreview();
		foreach (var kvp in _activeAttachmentVisuals)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				kvp.Value.GetParent()?.RemoveChild(kvp.Value);
				kvp.Value.QueueFree();
			}
		}
		_activeAttachmentVisuals.Clear();
		var configured = GetConfiguredAttachments();
		foreach (var entry in configured)
		{
			if (string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				AttachVisualToAnchor(entry.SocketId, entry.AttachmentId, entry.Orientation.Position.ToGodotVector3(), entry.Orientation.RotationDegrees.ToGodotVector3(), entry.Orientation.ScaleVector.ToGodotVector3(), entry.Orientation.NormalOffset, entry.Index, null);
			}
		}
		foreach (var entry in configured)
		{
			if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				AttachVisualToAnchor(entry.SocketId, entry.AttachmentId, entry.Orientation.Position.ToGodotVector3(), entry.Orientation.RotationDegrees.ToGodotVector3(), entry.Orientation.ScaleVector.ToGodotVector3(), entry.Orientation.NormalOffset, entry.Index, entry.Orientation.ParentAttachmentId);
			}
		}
	}

	private Node3D AttachVisualToAnchor(string socketId, string attachmentId, Vector3 pos, Vector3 rot, Vector3 scale, float normalOffset, int index = -1, string? parentAttachmentId = null)
	{
		string normSocket = NormalizeSocketId(socketId);
		if (!_socketAnchorNodes.TryGetValue(normSocket, out var targetAnchor) || targetAnchor == null || !GodotObject.IsInstanceValid(targetAnchor)) return null;

		string key = GetAttachmentKey(normSocket, attachmentId, index, parentAttachmentId);
		RemoveOldAttachmentVisual(key);

		if (string.IsNullOrEmpty(attachmentId) || attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase) || attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;

		Node3D loaded = Realm.Client.Unit3D.ResolveAndInstantiateAttachment(attachmentId, out _, out _, out _);
		if (loaded == null) return null;

		ConfigureLoadedVisual(loaded, attachmentId, key, pos, rot, scale, normalOffset);
		Node3D attachTarget = GetAttachTarget(targetAnchor, parentAttachmentId);

		attachTarget.AddChild(loaded);
		_activeAttachmentVisuals[key] = loaded;
		return loaded;
	}

	private void RemoveOldAttachmentVisual(string key)
	{
		if (_activeAttachmentVisuals.TryGetValue(key, out var oldNode) && GodotObject.IsInstanceValid(oldNode))
		{
			oldNode.GetParent()?.RemoveChild(oldNode);
			oldNode.QueueFree();
			_activeAttachmentVisuals.Remove(key);
		}
	}

	private void ConfigureLoadedVisual(Node3D loaded, string attachmentId, string key, Vector3 pos, Vector3 rot, Vector3 scale, float normalOffset)
	{
		string cleanAttId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
		loaded.Name = $"AttVisual_{key}";
		loaded.SetMeta("AttachmentId", attachmentId);
		loaded.SetMeta("CleanAttachmentId", cleanAttId);
		loaded.Position = pos + (Vector3.Up * normalOffset);
		loaded.RotationDegrees = rot;
		loaded.Scale = scale == Vector3.Zero ? Vector3.One : scale;
	}

	private Node3D GetAttachTarget(Node3D targetAnchor, string? parentAttachmentId)
	{
		if (string.IsNullOrEmpty(parentAttachmentId)) return targetAnchor;

		var parentMesh = Realm.Client.Unit3D.FindAttachmentInNode(targetAnchor, parentAttachmentId)
		                 ?? (_previewModel != null ? Realm.Client.Unit3D.FindAttachmentInNode(_previewModel, parentAttachmentId) : null);
		
		return parentMesh ?? targetAnchor;
	}

	private void AttachModelToSocket(string socketId, string attachmentId, Vector3 pos, Vector3 rot, Vector3 scale, float normalOffset)
	{
		_currentSocketId = NormalizeSocketId(socketId);
		_currentAttachmentId = attachmentId;
		_currentPosOffset = pos;
		_currentRotOffset = rot;
		_currentScaleOffset = scale;
		_currentNormalOffset = normalOffset;
		PreviewCurrentAttachment();
	}

	private void UpdateActiveAttachmentTransform()
	{
		if (_currentActiveModelNode != null && GodotObject.IsInstanceValid(_currentActiveModelNode))
		{
			_currentActiveModelNode.Position = _currentPosOffset + (Vector3.Up * _currentNormalOffset);
			_currentActiveModelNode.RotationDegrees = _currentRotOffset;
			_currentActiveModelNode.Scale = _currentScaleOffset;
		}
		else if (_socketModelNodes.TryGetValue(_currentSocketId, out var fallback) && GodotObject.IsInstanceValid(fallback))
		{
			fallback.Position = _currentPosOffset + (Vector3.Up * _currentNormalOffset);
			fallback.RotationDegrees = _currentRotOffset;
			fallback.Scale = _currentScaleOffset;
		}
	}

	private void RebuildConfiguredAttachmentsUI()
	{
		if (_configuredAttachmentsContainer == null) return;

		foreach (Node child in _configuredAttachmentsContainer.GetChildren())
		{
			child.QueueFree();
		}

		var configured = GetConfiguredAttachments();
		if (configured.Count == 0)
		{
			var emptyLabel = new Label
			{
				Text = TranslationServer.Translate("No attachments configured on this object."),
				AutowrapMode = TextServer.AutowrapMode.Word
			};
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			emptyLabel.AddThemeFontSizeOverride("font_size", 11);
			_configuredAttachmentsContainer.AddChild(emptyLabel);
			return;
		}

		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			var card = new PanelContainer();
			card.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 8);

			string parentAttId = entry.Orientation.ParentAttachmentId;
			bool isChild = !string.IsNullOrEmpty(parentAttId);

			string badgeText = isChild
				? $"[{entry.SocketId} ➔ {System.IO.Path.GetFileNameWithoutExtension(parentAttId)}]"
				: $"[{entry.SocketId}]";

			var badge = new Label
			{
				Text = badgeText,
				CustomMinimumSize = new Vector2(isChild ? 130 : 85, 0),
				ClipText = true
			};
			badge.AddThemeColorOverride("font_color", isChild ? new Color(0.9f, 0.6f, 1.0f) : UIStyle.ColorCyanGlow);
			badge.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(badge);

			string displayName = entry.AttachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? $"✨ {entry.AttachmentId.Substring(4)}"
				: $"🗡️ {entry.AttachmentId}";

			var nameLbl = new Label
			{
				Text = displayName,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
				ClipText = true
			};
			nameLbl.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			nameLbl.AddThemeFontSizeOverride("font_size", 11);
			row.AddChild(nameLbl);

			var summaryLbl = new Label
			{
				Text = $"Pos: ({entry.Orientation.PositionX:F2},{entry.Orientation.PositionY:F2},{entry.Orientation.PositionZ:F2})",
				CustomMinimumSize = new Vector2(130, 0),
				ClipText = true
			};
			summaryLbl.AddThemeColorOverride("font_color", new Color(0.65f, 0.65f, 0.7f));
			summaryLbl.AddThemeFontSizeOverride("font_size", 10);
			row.AddChild(summaryLbl);

			string capturedSocket = entry.SocketId;
			string capturedAtt = entry.AttachmentId;
			string? capturedParent = entry.Orientation.ParentAttachmentId;
			int capturedIndex = entry.Index;

			var btnEdit = new Button();
			btnEdit.Set("icon_max_width", 0);
			btnEdit.Text = "✏️ " + TranslationServer.Translate("Edit");
			btnEdit.AddThemeFontSizeOverride("font_size", 11);
			btnEdit.CustomMinimumSize = new Vector2(55, 22);
			btnEdit.FocusMode = Control.FocusModeEnum.None;
			btnEdit.TooltipText = TranslationServer.Translate("Select and tune transform in sliders");
			btnEdit.Pressed += () => SelectConfiguredAttachment(capturedSocket, capturedAtt, capturedIndex);
			row.AddChild(btnEdit);

			var btnRemove = new Button();
			btnRemove.Set("icon_max_width", 0);
			btnRemove.Text = "🗑️ " + TranslationServer.Translate("Remove");
			btnRemove.AddThemeFontSizeOverride("font_size", 11);
			btnRemove.AddThemeColorOverride("font_color", new Color(1.0f, 0.45f, 0.45f));
			btnRemove.CustomMinimumSize = new Vector2(65, 22);
			btnRemove.FocusMode = Control.FocusModeEnum.None;
			btnRemove.TooltipText = TranslationServer.Translate("Detach and remove this attachment");
			btnRemove.Pressed += () => RemoveAttachment(capturedSocket, capturedAtt, capturedParent);
			row.AddChild(btnRemove);

			card.AddChild(row);
			_configuredAttachmentsContainer.AddChild(card);
		}
	}

	private void SelectConfiguredAttachment(string socketId, string attachmentId, int index = -1)
	{
		ClearUncommittedPreview();
		ResetConfiguredVisualTransforms();

		_isUpdatingUI = true;
		_currentSocketId = NormalizeSocketId(socketId);
		_currentAttachmentId = attachmentId;

		UpdateSocketDropdownSelection();
		UpdateAttachmentPickerSelection(attachmentId);

		_isUpdatingUI = false;

		ConfiguredAttachmentEntry? matched = FindMatchedConfiguredAttachment(index);

		if (matched.HasValue)
		{
			ApplyConfiguredOrientation(matched.Value.Orientation);
			UpdateSliderDisplayValues();
		}
		else
		{
			LoadAttachmentOrientationIntoSliders(_targetObjectId, _currentSocketId, _currentAttachmentId);
		}

		UpdateParentDropdown();
		UpdateActiveVisualNode(index);

		_uncommittedPreviewNode = null;
		_uncommittedPreviewKey = null;
		UpdateActiveAttachmentTransform();
	}

	private void UpdateSocketDropdownSelection()
	{
		for (int i = 0; i < _currentAvailableSockets.Count; i++)
		{
			if (_currentAvailableSockets[i].SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase))
			{
				_optSocket.Selected = i;
				break;
			}
		}
	}

	private void UpdateAttachmentPickerSelection(string attachmentId)
	{
		string cleanId = GetCleanAttachmentId(attachmentId);
		for (int i = 0; i < _availableAttachments.Count; i++)
		{
			string item = _availableAttachments[i];
			if (item.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
			    item.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
			    System.IO.Path.GetFileNameWithoutExtension(item).Equals(cleanId, StringComparison.OrdinalIgnoreCase))
			{
				_optAttachmentPicker.Selected = i;
				break;
			}
		}
	}

	private ConfiguredAttachmentEntry? FindMatchedConfiguredAttachment(int index)
	{
		if (index < 0) return null;
		var configured = GetConfiguredAttachments();
		for (int i = 0; i < configured.Count; i++)
		{
			if (configured[i].Index == index && configured[i].SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase))
			{
				return configured[i];
			}
		}
		return null;
	}

	private void UpdateActiveVisualNode(int index)
	{
		string key = GetAttachmentKey(_currentSocketId, _currentAttachmentId, index, _currentParentAttachmentId);
		if (_activeAttachmentVisuals.TryGetValue(key, out var visualNode) && GodotObject.IsInstanceValid(visualNode))
		{
			_currentActiveModelNode = visualNode;
			_socketModelNodes[_currentSocketId] = visualNode;
		}
		else
		{
			var loaded = AttachVisualToAnchor(_currentSocketId, _currentAttachmentId, _currentPosOffset, _currentRotOffset, _currentScaleOffset, _currentNormalOffset, index, _currentParentAttachmentId);
			_currentActiveModelNode = loaded;
			if (loaded != null)
			{
				_socketModelNodes[_currentSocketId] = loaded;
			}
		}
	}

	private void RemoveAttachment(string socketId, string attachmentId, string? parentAttachmentId = null)
	{
		if (string.IsNullOrEmpty(_targetObjectId) || string.IsNullOrEmpty(attachmentId)) return;

		string normSocket = NormalizeSocketId(socketId);
		ClearUncommittedPreview();

		_workingAttachments.RemoveSocketAttachment(normSocket, attachmentId, parentAttachmentId);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Removed {0} from {1} ({2})."), attachmentId, _targetObjectId, normSocket));

		AttachAllConfiguredAttachments();
		UpdateParentDropdown();

		_currentActiveModelNode = null;
		RebuildConfiguredAttachmentsUI();
	}

	private void AddOrUpdateCurrentAttachment()
	{
		if (string.IsNullOrEmpty(_targetObjectId) || string.IsNullOrEmpty(_currentAttachmentId))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Please select an attachment to add."));
			return;
		}

		var orientation = new HandAttachmentOrientation
		{
			PositionX = _currentPosOffset.X,
			PositionY = _currentPosOffset.Y,
			PositionZ = _currentPosOffset.Z,
			PitchX = _currentRotOffset.X,
			YawY = _currentRotOffset.Y,
			RollZ = _currentRotOffset.Z,
			Scale = _currentScaleOffset.X,
			ScaleX = _currentScaleOffset.X,
			ScaleY = _currentScaleOffset.Y,
			ScaleZ = _currentScaleOffset.Z,
			NormalOffset = _currentNormalOffset,
			ParentAttachmentId = _currentParentAttachmentId
		};

		_workingAttachments.SetSocketOrientation(_currentSocketId, _currentAttachmentId, orientation);
		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Added {0} to {1} on socket {2}."), _currentAttachmentId, _targetObjectId, _currentSocketId));

		ClearUncommittedPreview();
		AttachAllConfiguredAttachments();
		UpdateParentDropdown();

		RebuildConfiguredAttachmentsUI();
	}

	protected override void OnApply()
	{
		if (!string.IsNullOrEmpty(_targetObjectId))
		{
			Hud?.SaveAllUnitObjectAttachments(_targetObjectId, _workingAttachments);
		}
		ClearPreviewModel();
		_onApplied?.Invoke(default);
	}

	protected override void OnCancel()
	{
		_workingAttachments = default;
		ClearPreviewModel();
	}

	public override void CloseDialog()
	{
		_workingAttachments = default;
		ClearPreviewModel();
		base.CloseDialog();
	}

	public static List<string> GetAvailableObjectAttachmentIds()
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var result = new List<string>();

		AddVfxRegistryAttachments(seen, result);

		string wsPath = !string.IsNullOrEmpty(Services.MapWorkspaceService.GetActiveWorkspacePath())
			? Services.MapWorkspaceService.GetActiveWorkspacePath()
			: ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		AddUnionedAssets(wsPath, seen, result);
		AddMetadataAssets(wsPath, seen, result);
		AddGameHostAttachments(seen, result);

		return result;
	}

	private static void AddVfxRegistryAttachments(HashSet<string> seen, List<string> result)
	{
		if (Realm.Client.Core.GameHost.VfxRegistry == null) return;
		foreach (var kvp in Realm.Client.Core.GameHost.VfxRegistry)
		{
			string vfxKey = kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? kvp.Key : $"vfx:{kvp.Key}";
			if (seen.Add(vfxKey)) result.Add(vfxKey);
		}
	}

	private static void AddUnionedAssets(string wsPath, HashSet<string> seen, List<string> result)
	{
		try
		{
			var assetsObj = MapAssetHelper.LoadAssets(wsPath);
			if (assetsObj == null) return;

			var itemsDict = assetsObj.GetCategory("Item");
			if (itemsDict != null)
			{
				foreach (var modelProp in itemsDict)
				{
					string id = System.IO.Path.GetFileNameWithoutExtension(modelProp.Key);
					if (seen.Add(id)) result.Add(id);
				}
			}

			var vfxDict = assetsObj.GetCategory("Spritesheet");
			if (vfxDict != null)
			{
				foreach (var prop in vfxDict)
				{
					string vfxKey = prop.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? prop.Key : $"vfx:{prop.Key}";
					if (seen.Add(vfxKey)) result.Add(vfxKey);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectAttachmentDialog] Error loading unioned assets: {ex.Message}");
		}
	}

	private static void AddMetadataAssets(string wsPath, HashSet<string> seen, List<string> result)
	{
		try
		{
			var metadata = MetadataService.Instance.LoadMetadata(wsPath);
			if (metadata.Templates == null) return;

			ProcessTemplates(metadata.Templates, seen, result);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectAttachmentDialog] Error scanning metadata: {ex.Message}");
		}
	}

	private static void ProcessTemplates(Realm.Shared.Metadata.TemplateContainer templates, HashSet<string> seen, List<string> result)
	{
		if (templates.Items != null)
			foreach (var it in templates.Items) TryAddTemplateId(it.TemplateID, seen, result);
		
		if (templates.Weapons != null)
			foreach (var wpn in templates.Weapons) TryAddTemplateId(wpn.TemplateID, seen, result);

		if (templates.Attachments != null)
			foreach (var att in templates.Attachments) TryAddTemplateId(att.AttachmentId, seen, result);

		if (templates.Vfx != null)
			foreach (var vfx in templates.Vfx) TryAddVfx(vfx.VfxId, seen, result);
	}

	private static void TryAddVfx(string? vfxId, HashSet<string> seen, List<string> result)
	{
		if (string.IsNullOrEmpty(vfxId)) return;
		string vfxKey = $"vfx:{vfxId}";
		if (seen.Add(vfxKey)) result.Add(vfxKey);
	}

	private static void TryAddTemplateId(string? templateId, HashSet<string> seen, List<string> result)
	{
		if (string.IsNullOrEmpty(templateId)) return;
		string cleanId = System.IO.Path.GetFileNameWithoutExtension(templateId);
		if (seen.Add(cleanId)) result.Add(cleanId);
	}

	private static void AddGameHostAttachments(HashSet<string> seen, List<string> result)
	{
		if (Realm.Client.Core.GameHost.AttachmentRegistry == null) return;
		foreach (var kvp in Realm.Client.Core.GameHost.AttachmentRegistry)
		{
			string id = System.IO.Path.GetFileNameWithoutExtension(kvp.Key);
			if (seen.Add(id)) result.Add(id);
		}
	}
}