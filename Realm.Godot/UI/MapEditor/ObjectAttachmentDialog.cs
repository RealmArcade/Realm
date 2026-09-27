using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Godot.Animation;
using Realm.Godot.Utils;
using Realm.Godot.VFX;
using Realm.Godot.Services;

public partial class ObjectAttachmentDialog : FloatingDialogBase
{
	private SubViewportContainer _viewportContainer;
	private SubViewport _subViewport;
	private Camera3D _camera;
	private DirectionalLight3D _light;
	private Node3D _previewSceneRoot;
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

	private bool _isOrbiting;
	private bool _isPanning;
	private Vector2 _lastMousePosition;
	private float _cameraYaw = 0f;
	private float _cameraPitch = 0.2f;
	private float _cameraDistance = 3.5f;
	private const float DefaultDistance = 3.5f;
	private Vector3 _targetPosition = new Vector3(0f, 1.0f, 0f);

	private Action<GameHost.HandAttachmentOrientation> _onApplied;
	private GameHost.UnitObjectAttachments? _initialSnapshot;
	private GameHost.UnitObjectAttachments _workingAttachments;

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
		BuildControls();
	}

	private void BuildControls()
	{
		_viewportContainer = Add3DViewportContainer(BodyContainer, new Vector2(530, 230), out _subViewport, out _camera, out _light);
		_viewportContainer.GuiInput += OnViewportGuiInput;
		_viewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		_previewSceneRoot = new Node3D { Name = "PreviewRoot" };
		_subViewport.AddChild(_previewSceneRoot);

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		var presetRow = new HBoxContainer();
		presetRow.AddThemeConstantOverride("separation", 4);

		AddButton(presetRow, TranslationServer.Translate("Front"), () => SetCameraPreset(0f, 0f), "View front", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Side"), () => SetCameraPreset(90f, 0f), "View side", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Back"), () => SetCameraPreset(180f, 0f), "View back", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Iso"), () => SetCameraPreset(45f, 25f), "Isometric view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Top"), () => SetCameraPreset(0f, 85f), "Top-down view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("⟲ Reset"), () => ResetCameraDefault(), "Reset camera zoom and position", 10, new Vector2(0, 22));

		topControlsVBox.AddChild(presetRow);

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

		topControlsVBox.AddChild(infoRow);

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

		topControlsVBox.AddChild(socketRow);

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

		AddButton(pickerRow, "✨ " + TranslationServer.Translate("VFX Studio"), () =>
		{
			Hud?.OpenVfxManagerDialog((cfg) =>
			{
				RefreshAttachmentList();
				_currentAttachmentId = cfg.VfxId;
				for (int i = 0; i < _availableAttachments.Count; i++)
				{
					if (_availableAttachments[i].Equals(cfg.VfxId, StringComparison.OrdinalIgnoreCase) ||
						_availableAttachments[i].Equals($"vfx:{cfg.VfxId}", StringComparison.OrdinalIgnoreCase))
					{
						_isUpdatingUI = true;
						_optAttachmentPicker.Selected = i;
						_currentAttachmentId = _availableAttachments[i];
						_isUpdatingUI = false;
						break;
					}
				}
				UpdateParentDropdown();
				PreviewCurrentAttachment();
			}, _currentAttachmentId?.Replace("vfx:", ""));
		}, "Open Procedural VFX Studio", 10, new Vector2(100, 24));

		topControlsVBox.AddChild(pickerRow);

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

		var rotResultX = AddSlider(rightCol, TranslationServer.Translate("Pitch X (deg):"), -180f, 180f, 1f, 0f, (v) => { _currentRotOffset.X = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0", 95f);
		_sliderRotX = rotResultX.Slider; _lblRotX = rotResultX.ValueLabel;

		var rotResultY = AddSlider(rightCol, TranslationServer.Translate("Yaw Y (deg):"), -180f, 180f, 1f, 0f, (v) => { _currentRotOffset.Y = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0", 95f);
		_sliderRotY = rotResultY.Slider; _lblRotY = rotResultY.ValueLabel;

		var rotResultZ = AddSlider(rightCol, TranslationServer.Translate("Roll Z (deg):"), -180f, 180f, 1f, 0f, (v) => { _currentRotOffset.Z = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0", 95f);
		_sliderRotZ = rotResultZ.Slider; _lblRotZ = rotResultZ.ValueLabel;

		var scaleResultX = AddSlider(leftCol, TranslationServer.Translate("Scale X:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) => { _currentScaleOffset.X = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.00", 90f);
		_sliderScaleX = scaleResultX.Slider; _lblScaleX = scaleResultX.ValueLabel;

		var scaleResultY = AddSlider(leftCol, TranslationServer.Translate("Scale Y:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) => { _currentScaleOffset.Y = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.00", 90f);
		_sliderScaleY = scaleResultY.Slider; _lblScaleY = scaleResultY.ValueLabel;

		var scaleResultZ = AddSlider(leftCol, TranslationServer.Translate("Scale Z:"), 0.05f, 5.0f, 0.05f, 1.0f, (v) => { _currentScaleOffset.Z = v; UpdateActiveAttachmentTransform(); SyncWorkingAttachmentIfConfigured(); }, "0.00", 90f);
		_sliderScaleZ = scaleResultZ.Slider; _lblScaleZ = scaleResultZ.ValueLabel;

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

		AddSectionHeader(BodyContainer, "📎 " + TranslationServer.Translate("CONFIGURED ATTACHMENTS & VFX"), new Color(0.85f, 0.75f, 0.4f));
		_configuredAttachmentsContainer = CreateScrollBody(150);

		CancelButton.Text = TranslationServer.Translate("CANCEL");
		ApplyButton.Text = TranslationServer.Translate("APPLY");
	}

	public void OpenForUnitAndAttachment(
		string unitId,
		string attachmentId = null,
		string socket = null,
		Node3D sourceModel = null,
		Action<GameHost.HandAttachmentOrientation> onApplied = null)
	{
		OpenForTarget(unitId, attachmentId, socket, sourceModel, onApplied);
	}

	public void OpenForTarget(
		string targetId,
		string attachmentId = null,
		string socket = null,
		Node3D sourceModel = null,
		Action<GameHost.HandAttachmentOrientation> onApplied = null)
	{
		_onApplied = onApplied;

		if (string.IsNullOrEmpty(targetId))
		{
			if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.Count > 0)
			{
				targetId = GameHost.UnitRegistry.Keys.First();
			}
			else if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.Count > 0)
			{
				targetId = GameHost.BuildingRegistry.Keys.First();
			}
		}

		_targetObjectId = targetId ?? string.Empty;
		_isTargetBuilding = GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.ContainsKey(_targetObjectId);

		if (GameHost.TryGetUnitOrBuildingMetadata(_targetObjectId, out var meta))
		{
			_initialSnapshot = meta.ObjectAttachments?.Clone();
		}
		else
		{
			_initialSnapshot = null;
		}

		_workingAttachments = _initialSnapshot?.Clone() ?? new GameHost.UnitObjectAttachments();

		string defaultSocket = _isTargetBuilding ? "Center" : "RightHand";
		_currentSocketId = NormalizeSocketId(string.IsNullOrEmpty(socket) ? defaultSocket : socket);
		_currentAttachmentId = attachmentId ?? string.Empty;

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

		RefreshAttachmentList();
		LoadTargetModelAndRig(_targetObjectId, sourceModel);
		LoadAttachmentOrientationIntoSliders(_targetObjectId, _currentSocketId, _currentAttachmentId, _currentParentAttachmentId);
		UpdateParentDropdown();
		RebuildConfiguredAttachmentsUI();

		ResetCameraDefault();
		OpenDialog();
	}

	public static string NormalizeSocketId(string socket)
	{
		if (string.IsNullOrEmpty(socket)) return "RightHand";
		string s = socket.ToLowerInvariant().Replace("_", "").Replace(" ", "");
		if (s.Equals("top") || s.Equals("roof")) return "Top";
		if (s.Equals("base")) return "Base";
		if (s.Equals("pivot") || s.Equals("origin")) return "Pivot";
		if (s.Contains("ground") || s.Contains("footprint")) return "Ground";
		if (s.Contains("overhead") || s.Contains("crown")) return "Overhead";
		if (s.Contains("chest")) return "Chest";
		if (s.Contains("center") || s.Contains("centerofmass") || s.Contains("middle")) return "Center";
		if (s.Contains("lefthand")) return "LeftHand";
		if (s.Contains("righthand")) return "RightHand";
		if (s.Contains("hips") || s.Contains("pelvis") || s.Contains("root")) return "Hips";
		if (s.Contains("head")) return "Head";
		if (s.Contains("leftfoot")) return "LeftFoot";
		if (s.Contains("rightfoot")) return "RightFoot";
		return "RightHand";
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
			if (!entry.SocketId.Equals(normSocket, StringComparison.OrdinalIgnoreCase)) continue;
			if (string.Equals(entry.AttachmentId, _currentAttachmentId, StringComparison.OrdinalIgnoreCase)) continue;
			if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId)) continue;

			string cleanId = System.IO.Path.GetFileNameWithoutExtension(entry.AttachmentId);
			if (_availableParents.Contains(cleanId) || _availableParents.Contains(entry.AttachmentId)) continue;

			int itemIdx = _availableParents.Count;
			_availableParents.Add(entry.AttachmentId);
			string displayName = entry.AttachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? $"✨ {entry.AttachmentId.Substring(4)}"
				: $"🗡️ {cleanId}";
			_optParent.AddItem(string.Format(TranslationServer.Translate("Mesh: {0}"), displayName), itemIdx);

			if (!string.IsNullOrEmpty(_currentParentAttachmentId) &&
				(string.Equals(_currentParentAttachmentId, entry.AttachmentId, StringComparison.OrdinalIgnoreCase) ||
				 string.Equals(_currentParentAttachmentId, cleanId, StringComparison.OrdinalIgnoreCase)))
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

	private void RefreshAttachmentList()
	{
		_availableAttachments = GetAvailableObjectAttachmentIds();
		if (_optAttachmentPicker != null)
		{
			_isUpdatingUI = true;
			_optAttachmentPicker.Clear();
			int selectedIdx = 0;
			string cleanCurrent = string.IsNullOrEmpty(_currentAttachmentId)
				? ""
				: (_currentAttachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
					? _currentAttachmentId
					: System.IO.Path.GetFileNameWithoutExtension(_currentAttachmentId));

			for (int i = 0; i < _availableAttachments.Count; i++)
			{
				string item = _availableAttachments[i];
				string display = item.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
					? $"✨ {item.Substring(4)}"
					: $"🗡️ {item}";
				_optAttachmentPicker.AddItem(display, i);

				if (!string.IsNullOrEmpty(cleanCurrent))
				{
					string cleanItem = item.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
						? item
						: System.IO.Path.GetFileNameWithoutExtension(item);
					if (item.Equals(_currentAttachmentId, StringComparison.OrdinalIgnoreCase) ||
						cleanItem.Equals(cleanCurrent, StringComparison.OrdinalIgnoreCase))
					{
						selectedIdx = i;
					}
				}
			}

			if (_availableAttachments.Count > 0)
			{
				_optAttachmentPicker.Selected = selectedIdx;
				_currentAttachmentId = _availableAttachments[selectedIdx];
			}
			_isUpdatingUI = false;
		}
	}

	private void LoadAttachmentOrientationIntoSliders(string targetId, string socketId, string attachmentId, string? parentAttachmentId = null)
	{
		GameHost.HandAttachmentOrientation unitOrient = default;
		bool hasOrient = false;

		if (!string.IsNullOrEmpty(targetId))
		{
			var configured = GetConfiguredAttachments();
			string normSocket = NormalizeSocketId(socketId);
			string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? attachmentId
				: System.IO.Path.GetFileNameWithoutExtension(attachmentId);

			for (int i = 0; i < configured.Count; i++)
			{
				var entry = configured[i];
				if (entry.SocketId.Equals(normSocket, StringComparison.OrdinalIgnoreCase))
				{
					bool keyMatch = entry.AttachmentId.Equals(attachmentId, StringComparison.OrdinalIgnoreCase) ||
						entry.AttachmentId.Equals(cleanId, StringComparison.OrdinalIgnoreCase) ||
						System.IO.Path.GetFileNameWithoutExtension(entry.AttachmentId).Equals(cleanId, StringComparison.OrdinalIgnoreCase);

					if (keyMatch)
					{
						if (parentAttachmentId == null || string.Equals(entry.Orientation.ParentAttachmentId ?? string.Empty, parentAttachmentId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
						{
							unitOrient = entry.Orientation;
							hasOrient = true;
							break;
						}
					}
				}
			}
		}

		if (hasOrient)
		{
			_currentPosOffset = unitOrient.Position;
			_currentRotOffset = unitOrient.RotationDegrees;
			_currentScaleOffset = unitOrient.ScaleVector;
			_currentNormalOffset = unitOrient.NormalOffset;
			_currentParentAttachmentId = unitOrient.ParentAttachmentId;
		}
		else if (!string.IsNullOrEmpty(attachmentId) && GameHost.AttachmentRegistry.TryGetValue(attachmentId, out var attMeta))
		{
			_currentPosOffset = attMeta.PositionOffset;
			_currentRotOffset = attMeta.RotationOffset;
			_currentScaleOffset = Vector3.One * (attMeta.Scale <= 0f ? 1.0f : attMeta.Scale);
			_currentNormalOffset = 0.0f;
		}
		else
		{
			_currentPosOffset = Vector3.Zero;
			_currentRotOffset = Vector3.Zero;
			_currentScaleOffset = Vector3.One;
			_currentNormalOffset = 0.0f;
		}

		UpdateSliderDisplayValues();
	}

	private void UpdateSliderDisplayValues()
	{
		if (_sliderPosX != null) { _sliderPosX.Value = _currentPosOffset.X; _lblPosX.Text = _currentPosOffset.X.ToString("F3"); }
		if (_sliderPosY != null) { _sliderPosY.Value = _currentPosOffset.Y; _lblPosY.Text = _currentPosOffset.Y.ToString("F3"); }
		if (_sliderPosZ != null) { _sliderPosZ.Value = _currentPosOffset.Z; _lblPosZ.Text = _currentPosOffset.Z.ToString("F3"); }

		if (_sliderRotX != null) { _sliderRotX.Value = _currentRotOffset.X; _lblRotX.Text = _currentRotOffset.X.ToString("F0"); }
		if (_sliderRotY != null) { _sliderRotY.Value = _currentRotOffset.Y; _lblRotY.Text = _currentRotOffset.Y.ToString("F0"); }
		if (_sliderRotZ != null) { _sliderRotZ.Value = _currentRotOffset.Z; _lblRotZ.Text = _currentRotOffset.Z.ToString("F0"); }

		if (_sliderScaleX != null) { _sliderScaleX.Value = _currentScaleOffset.X; _lblScaleX.Text = _currentScaleOffset.X.ToString("F2"); }
		if (_sliderScaleY != null) { _sliderScaleY.Value = _currentScaleOffset.Y; _lblScaleY.Text = _currentScaleOffset.Y.ToString("F2"); }
		if (_sliderScaleZ != null) { _sliderScaleZ.Value = _currentScaleOffset.Z; _lblScaleZ.Text = _currentScaleOffset.Z.ToString("F2"); }

		if (_sliderNormalOffset != null) { _sliderNormalOffset.Value = _currentNormalOffset; _lblNormalOffset.Text = _currentNormalOffset.ToString("F3"); }
	}

	private void SyncWorkingAttachmentIfConfigured()
	{
		if (string.IsNullOrEmpty(_targetObjectId) || string.IsNullOrEmpty(_currentAttachmentId)) return;
		if (IsAttachmentConfigured(_currentSocketId, _currentAttachmentId, _currentParentAttachmentId))
		{
			var orientation = new GameHost.HandAttachmentOrientation
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
			_currentPosOffset = Vector3.Zero;
			_currentRotOffset = Vector3.Zero;
			_currentNormalOffset = 0.0f;
			UpdateSliderDisplayValues();
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
			return;
		}

		_socketAnchorNodes.TryGetValue(_currentSocketId, out var targetAnchor);
		if (targetAnchor == null || !GodotObject.IsInstanceValid(targetAnchor)) return;

		Node3D targetNode = _currentActiveModelNode;
		if (targetNode == null || !GodotObject.IsInstanceValid(targetNode))
		{
			PreviewCurrentAttachment();
			targetNode = _currentActiveModelNode;
			if (targetNode == null || !GodotObject.IsInstanceValid(targetNode)) return;
		}

		var currentDef = _currentAvailableSockets.FirstOrDefault(s => s.SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase));
		if (currentDef.IsPseudoSocket)
		{
			if (_currentSocketId.Equals("Ground", StringComparison.OrdinalIgnoreCase) || _currentSocketId.Equals("Base", StringComparison.OrdinalIgnoreCase))
			{
				_currentPosOffset = new Vector3(0f, 0.02f, 0f);
				_currentRotOffset = Vector3.Zero;
			}
			else
			{
				_currentPosOffset = Vector3.Zero;
				_currentRotOffset = Vector3.Zero;
			}
			_currentNormalOffset = 0.0f;
			UpdateSliderDisplayValues();
			UpdateActiveAttachmentTransform();
			SyncWorkingAttachmentIfConfigured();
			return;
		}

		Transform3D savedTransform = targetNode.Transform;
		targetNode.Transform = Transform3D.Identity;

		Aabb localAabb = CalculateAttachmentLocalAabb(targetNode);
		targetNode.Transform = savedTransform;

		Vector3 effectiveScale = _currentScaleOffset == Vector3.Zero ? Vector3.One : _currentScaleOffset;
		Vector3 size = localAabb.Size;
		Vector3 scaledSize = new Vector3(size.X * Mathf.Abs(effectiveScale.X), size.Y * Mathf.Abs(effectiveScale.Y), size.Z * Mathf.Abs(effectiveScale.Z));

		int primaryAxis = 1;
		if (scaledSize.X > scaledSize.Y && scaledSize.X > scaledSize.Z) primaryAxis = 0;
		else if (scaledSize.Z > scaledSize.Y && scaledSize.Z > scaledSize.X) primaryAxis = 2;

		Vector3 localGripPoint;
		if (primaryAxis == 0)
		{
			float gripX = localAabb.Position.X < -0.05f && localAabb.End.X > 0.05f
				? localAabb.Position.X + size.X * 0.15f
				: localAabb.Position.X;
			localGripPoint = new Vector3(gripX, localAabb.GetCenter().Y, localAabb.GetCenter().Z);
		}
		else if (primaryAxis == 2)
		{
			float gripZ = localAabb.Position.Z < -0.05f && localAabb.End.Z > 0.05f
				? localAabb.Position.Z + size.Z * 0.15f
				: localAabb.Position.Z;
			localGripPoint = new Vector3(localAabb.GetCenter().X, localAabb.GetCenter().Y, gripZ);
		}
		else
		{
			float gripY = localAabb.Position.Y < -0.05f && localAabb.End.Y > 0.05f
				? localAabb.Position.Y + size.Y * 0.15f
				: localAabb.Position.Y;
			localGripPoint = new Vector3(localAabb.GetCenter().X, gripY, localAabb.GetCenter().Z);
		}

		Vector3 handPos = targetAnchor.GlobalPosition;
		Vector3 camPos = _camera != null ? _camera.GlobalPosition : new Vector3(0f, 1.5f, 3f);
		Vector3 dirToCam = (camPos - handPos).Normalized();

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

		Vector3 desiredTopDir = (dirToCam * 0.7f + Vector3.Up * 0.5f + wristNormal * 0.3f).Normalized();

		Vector3 desiredRight = desiredTopDir.Cross(Vector3.Up).Normalized();
		if (desiredRight.LengthSquared() < 0.001f)
		{
			Vector3 camRight = _camera != null ? _camera.GlobalTransform.Basis.X.Normalized() : Vector3.Right;
			desiredRight = desiredTopDir.Cross(camRight).Normalized();
		}

		if (currentDef.AssociatedBone == HumanoidBone.LeftHand)
		{
			desiredRight = -desiredRight;
		}

		Vector3 desiredNormal = desiredRight.Cross(desiredTopDir).Normalized();

		Basis desiredWorldBasis;
		if (primaryAxis == 0)
		{
			desiredWorldBasis = new Basis(desiredTopDir, desiredNormal, desiredRight);
		}
		else if (primaryAxis == 2)
		{
			desiredWorldBasis = new Basis(desiredRight, desiredNormal, desiredTopDir);
		}
		else
		{
			desiredWorldBasis = new Basis(desiredRight, desiredTopDir, desiredNormal);
		}

		Basis handBasis = targetAnchor.GlobalTransform.Basis.Orthonormalized();
		Basis desiredLocalBasis = Mathf.Abs(handBasis.Determinant()) < 0.0001f 
			? desiredWorldBasis.Orthonormalized() 
			: (handBasis.Inverse() * desiredWorldBasis).Orthonormalized();

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

				if (Mathf.Abs(relXform.Basis.Determinant()) > 0.0001f)
				{
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

		Node3D modelToInstantiate = null;

		if (sourceModel != null && GodotObject.IsInstanceValid(sourceModel))
		{
			modelToInstantiate = (Node3D)sourceModel.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
		}
		else if (!string.IsNullOrEmpty(targetId))
		{
			string modelPath = null;
			if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.TryGetValue(targetId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
			{
				modelPath = meta.ModelPath;
			}
			else if (GameHost.BuildingRegistry != null && GameHost.BuildingRegistry.TryGetValue(targetId, out var bldMeta) && !string.IsNullOrEmpty(bldMeta.ModelPath))
			{
				modelPath = bldMeta.ModelPath;
			}
			else if (GameHost.PropRegistry != null &&
				(GameHost.PropRegistry.TryGetValue(targetId, out var pMeta) || GameHost.PropRegistry.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(targetId), out pMeta)))
			{
				modelPath = pMeta.ModelPath;
			}

			if (string.IsNullOrEmpty(modelPath)) modelPath = targetId;
			var cached = ModelCache.GetModel(modelPath);
			if (cached is Node3D n3d)
			{
				modelToInstantiate = (Node3D)n3d.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
			}
		}

		if (modelToInstantiate == null) return;

		RemoveAllExistingAttachments(modelToInstantiate);

		_previewModel = modelToInstantiate;
		_previewModel.Position = Vector3.Zero;
		_previewModel.Rotation = Vector3.Zero;
		_previewModel.Scale = Vector3.One;
		_previewSceneRoot.AddChild(_previewModel);

		var skeleton = SkeletonValidator.FindSkeleton(_previewModel);
		Aabb modelAabb = CalculateAttachmentLocalAabb(_previewModel);

		_currentAvailableSockets.Clear();

		if (skeleton != null && !_isTargetBuilding)
		{
			if (sourceModel != null && GodotObject.IsInstanceValid(sourceModel))
			{
				var srcSkeleton = SkeletonValidator.FindSkeleton(sourceModel);
				if (srcSkeleton != null)
				{
					int count = Math.Min(srcSkeleton.GetBoneCount(), skeleton.GetBoneCount());
					for (int i = 0; i < count; i++)
					{
						skeleton.SetBonePosePosition(i, srcSkeleton.GetBonePosePosition(i));
						skeleton.SetBonePoseRotation(i, srcSkeleton.GetBonePoseRotation(i));
						skeleton.SetBonePoseScale(i, srcSkeleton.GetBonePoseScale(i));
					}
				}
			}

			_currentAvailableSockets.AddRange(RiggedSockets);

			foreach (var sock in _currentAvailableSockets)
			{
				if (sock.IsPseudoSocket)
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
				else if (sock.AssociatedBone.HasValue)
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
			}
		}
		else
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

		UpdateSocketDropdown();
		AttachAllConfiguredAttachments();
		if (!string.IsNullOrEmpty(_currentAttachmentId))
		{
			PreviewCurrentAttachment();
		}
		else
		{
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
		public GameHost.HandAttachmentOrientation Orientation;
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
		if (GameHost.TryGetUnitOrBuildingMetadata(targetObjectId, out var meta))
		{
			return GetConfiguredAttachmentsFromData(meta.ObjectAttachments, isBuilding);
		}
		return new List<ConfiguredAttachmentEntry>();
	}

	public static List<ConfiguredAttachmentEntry> GetConfiguredAttachmentsFromData(GameHost.UnitObjectAttachments? attsNode, bool isBuilding = false)
	{
		var list = new List<ConfiguredAttachmentEntry>();
		if (attsNode.HasValue)
		{
			var atts = attsNode.Value;
			void Collect(string socket, List<Dictionary<string, GameHost.HandAttachmentOrientation>>? sockList)
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

			if (isBuilding)
			{
				Collect("Center", atts.center ?? atts.chest);
				Collect("Top", atts.overhead ?? atts.head);
				Collect("Base", atts.ground ?? atts.root);
				Collect("Pivot", atts.pivot ?? atts.right_hand);
			}
			else
			{
				Collect("RightHand", atts.right_hand);
				Collect("LeftHand", atts.left_hand);
				Collect("Chest", atts.chest);
				Collect("Hips", atts.root);
				Collect("Head", atts.head);
				Collect("LeftFoot", atts.left_foot);
				Collect("RightFoot", atts.right_foot);
				Collect("Ground", atts.ground);
				Collect("Center", atts.center);
				Collect("Overhead", atts.overhead);
				Collect("Pivot", atts.pivot);
			}
		}

		return list;
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
				visualNode.Position = entry.Orientation.Position + (Vector3.Up * entry.Orientation.NormalOffset);
				visualNode.RotationDegrees = entry.Orientation.RotationDegrees;
				visualNode.Scale = entry.Orientation.ScaleVector == Vector3.Zero ? Vector3.One : entry.Orientation.ScaleVector;
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
				AttachVisualToAnchor(entry.SocketId, entry.AttachmentId, entry.Orientation.Position, entry.Orientation.RotationDegrees, entry.Orientation.ScaleVector, entry.Orientation.NormalOffset, entry.Index, null);
			}
		}
		foreach (var entry in configured)
		{
			if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				AttachVisualToAnchor(entry.SocketId, entry.AttachmentId, entry.Orientation.Position, entry.Orientation.RotationDegrees, entry.Orientation.ScaleVector, entry.Orientation.NormalOffset, entry.Index, entry.Orientation.ParentAttachmentId);
			}
		}
	}

	private Node3D AttachVisualToAnchor(string socketId, string attachmentId, Vector3 pos, Vector3 rot, Vector3 scale, float normalOffset, int index = -1, string? parentAttachmentId = null)
	{
		string normSocket = NormalizeSocketId(socketId);
		_socketAnchorNodes.TryGetValue(normSocket, out var targetAnchor);
		if (targetAnchor == null || !GodotObject.IsInstanceValid(targetAnchor)) return null;

		string key = GetAttachmentKey(normSocket, attachmentId, index, parentAttachmentId);
		if (_activeAttachmentVisuals.TryGetValue(key, out var oldNode) && GodotObject.IsInstanceValid(oldNode))
		{
			oldNode.GetParent()?.RemoveChild(oldNode);
			oldNode.QueueFree();
			_activeAttachmentVisuals.Remove(key);
		}

		if (string.IsNullOrEmpty(attachmentId) || attachmentId.Equals("null", StringComparison.OrdinalIgnoreCase) || attachmentId.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;

		Node3D loaded = Unit3D.ResolveAndInstantiateAttachment(attachmentId, out _, out _, out _);
		if (loaded != null)
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

			Node3D attachTarget = targetAnchor;
			if (!string.IsNullOrEmpty(parentAttachmentId))
			{
				var parentMesh = Unit3D.FindAttachmentInNode(targetAnchor, parentAttachmentId)
					?? (_previewModel != null ? Unit3D.FindAttachmentInNode(_previewModel, parentAttachmentId) : null);
				if (parentMesh != null)
				{
					attachTarget = parentMesh;
				}
			}

			attachTarget.AddChild(loaded);
			_activeAttachmentVisuals[key] = loaded;
		}
		return loaded;
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

		for (int i = 0; i < _currentAvailableSockets.Count; i++)
		{
			if (_currentAvailableSockets[i].SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase))
			{
				_optSocket.Selected = i;
				break;
			}
		}

		string cleanId = attachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
			? attachmentId
			: System.IO.Path.GetFileNameWithoutExtension(attachmentId);
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

		_isUpdatingUI = false;

		var configured = GetConfiguredAttachments();
		ConfiguredAttachmentEntry? matched = null;
		if (index >= 0)
		{
			for (int i = 0; i < configured.Count; i++)
			{
				if (configured[i].Index == index && configured[i].SocketId.Equals(_currentSocketId, StringComparison.OrdinalIgnoreCase))
				{
					matched = configured[i];
					break;
				}
			}
		}

		if (matched.HasValue)
		{
			_currentPosOffset = matched.Value.Orientation.Position;
			_currentRotOffset = matched.Value.Orientation.RotationDegrees;
			_currentScaleOffset = matched.Value.Orientation.ScaleVector == Vector3.Zero ? Vector3.One : matched.Value.Orientation.ScaleVector;
			_currentNormalOffset = matched.Value.Orientation.NormalOffset;
			_currentParentAttachmentId = matched.Value.Orientation.ParentAttachmentId;
			UpdateSliderDisplayValues();
		}
		else
		{
			LoadAttachmentOrientationIntoSliders(_targetObjectId, _currentSocketId, _currentAttachmentId);
		}

		UpdateParentDropdown();

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

		_uncommittedPreviewNode = null;
		_uncommittedPreviewKey = null;
		UpdateActiveAttachmentTransform();
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

		var orientation = new GameHost.HandAttachmentOrientation
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

		// 1. Registered custom VFX
		if (GameHost.VfxRegistry != null)
		{
			foreach (var kvp in GameHost.VfxRegistry)
			{
				string vfxKey = kvp.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
					? kvp.Key
					: $"vfx:{kvp.Key}";
				if (seen.Add(vfxKey))
				{
					result.Add(vfxKey);
				}
			}
		}

		string wsPath = !string.IsNullOrEmpty(MapWorkspaceService.GetActiveWorkspacePath())
			? MapWorkspaceService.GetActiveWorkspacePath()
			: ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);

		// 3. Unioned Assets from manifest/metadata (Items and VFX imported into map)
		try
		{
			var assetsObj = Realm.Godot.Utils.MapAssetHelper.LoadUnionedAssets(wsPath);
			if (assetsObj != null)
			{
				if (assetsObj["glb"] is JsonObject glbObj)
				{
					foreach (var subCat in glbObj)
					{
						string catName = subCat.Key;
						bool isItemFolder = catName.Equals("items", StringComparison.OrdinalIgnoreCase) ||
							catName.Equals("attachments", StringComparison.OrdinalIgnoreCase) ||
							catName.Equals("weapons", StringComparison.OrdinalIgnoreCase) ||
							catName.Equals("projectiles", StringComparison.OrdinalIgnoreCase) ||
							catName.Equals("rmesh_items", StringComparison.OrdinalIgnoreCase) ||
							catName.Equals("rmesh_attachments", StringComparison.OrdinalIgnoreCase) ||
							catName.Equals("rmesh_weapons", StringComparison.OrdinalIgnoreCase) ||
							catName.Equals("rmesh_projectiles", StringComparison.OrdinalIgnoreCase);

						if (subCat.Value is JsonObject modelsObj)
						{
							foreach (var modelProp in modelsObj)
							{
								string fileName = modelProp.Key;
								string id = System.IO.Path.GetFileNameWithoutExtension(fileName);
								bool isAttachment = isItemFolder;

								if (!isAttachment && modelProp.Value is JsonObject mObj)
								{
									string? at = mObj["asset_type"]?.ToString()
										?? mObj["AssetType"]?.ToString()
										?? mObj["default_asset_type"]?.ToString()
										?? mObj["type"]?.ToString();
									if (!string.IsNullOrEmpty(at) && Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(fileName, at, out string canonical, out _) && canonical.Equals("Item", StringComparison.OrdinalIgnoreCase))
									{
										isAttachment = true;
									}
								}

								if (isAttachment && seen.Add(id))
								{
									result.Add(id);
								}
							}
						}
					}
				}

				if (assetsObj["vfx"] is JsonObject vfxObj)
				{
					foreach (var prop in vfxObj)
					{
						string vfxKey = prop.Key.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase) ? prop.Key : $"vfx:{prop.Key}";
						if (seen.Add(vfxKey))
						{
							result.Add(vfxKey);
						}
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectAttachmentDialog] Error loading unioned assets: {ex.Message}");
		}

		try
		{
			var metadata = MetadataService.Instance.LoadMetadata(wsPath);

			if (metadata.CustomItems != null)
			{
				foreach (var it in metadata.CustomItems)
				{
					string val = it.ItemId ?? "";
					if (!string.IsNullOrEmpty(val))
					{
						string cleanId = System.IO.Path.GetFileNameWithoutExtension(val);
						if (seen.Add(cleanId)) result.Add(cleanId);
					}
				}
			}

			if (metadata.CustomWeapons != null)
			{
				foreach (var wpn in metadata.CustomWeapons)
				{
					string val = wpn.WeaponId ?? "";
					if (!string.IsNullOrEmpty(val))
					{
						string cleanId = System.IO.Path.GetFileNameWithoutExtension(val);
						if (seen.Add(cleanId)) result.Add(cleanId);
					}
				}
			}

			if (metadata.CustomAttachments != null)
			{
				foreach (var att in metadata.CustomAttachments)
				{
					string val = att.AttachmentId ?? "";
					if (!string.IsNullOrEmpty(val))
					{
						string cleanId = System.IO.Path.GetFileNameWithoutExtension(val);
						if (seen.Add(cleanId)) result.Add(cleanId);
					}
				}
			}

			if (metadata.CustomVfx != null)
			{
				foreach (var vfx in metadata.CustomVfx)
				{
					if (!string.IsNullOrEmpty(vfx.VfxId))
					{
						string vfxKey = $"vfx:{vfx.VfxId}";
						if (seen.Add(vfxKey)) result.Add(vfxKey);
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[ObjectAttachmentDialog] Error scanning metadata: {ex.Message}");
		}

		// 5. GameHost.AttachmentRegistry
		if (GameHost.AttachmentRegistry != null)
		{
			foreach (var kvp in GameHost.AttachmentRegistry)
			{
				string id = System.IO.Path.GetFileNameWithoutExtension(kvp.Key);
				if (seen.Add(id))
				{
					result.Add(id);
				}
			}
		}

		return result;
	}

	private void OnViewportGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				_isOrbiting = mouseButton.Pressed;
				_lastMousePosition = mouseButton.Position;
			}
			else if (mouseButton.ButtonIndex == MouseButton.Right || mouseButton.ButtonIndex == MouseButton.Middle)
			{
				_isPanning = mouseButton.Pressed;
				_lastMousePosition = mouseButton.Position;
			}
			else if (mouseButton.ButtonIndex == MouseButton.WheelUp && mouseButton.Pressed)
			{
				ZoomCamera(-1.0f);
			}
			else if (mouseButton.ButtonIndex == MouseButton.WheelDown && mouseButton.Pressed)
			{
				ZoomCamera(1.0f);
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion)
		{
			Vector2 delta = mouseMotion.Position - _lastMousePosition;
			_lastMousePosition = mouseMotion.Position;

			if (_isOrbiting)
			{
				_cameraYaw -= delta.X * 0.01f;
				_cameraPitch -= delta.Y * 0.01f;
				UpdateCameraTransform();
			}
			else if (_isPanning && _camera != null)
			{
				Vector3 camRight = _camera.GlobalTransform.Basis.X;
				Vector3 camUp = _camera.GlobalTransform.Basis.Y;
				float panSpeed = _cameraDistance * 0.0025f;
				_targetPosition -= (camRight * delta.X - camUp * delta.Y) * panSpeed;
				UpdateCameraTransform();
			}
		}
	}

	private void ZoomCamera(float direction)
	{
		float factor = direction > 0 ? 1.15f : 0.85f;
		_cameraDistance = Mathf.Clamp(_cameraDistance * factor, DefaultDistance * 0.15f, DefaultDistance * 6.0f);
		UpdateCameraTransform();
	}

	public void SetCameraPreset(float yawDegrees, float pitchDegrees)
	{
		_cameraYaw = Mathf.DegToRad(yawDegrees);
		_cameraPitch = Mathf.DegToRad(pitchDegrees);
		UpdateCameraTransform();
	}

	public void ResetCameraDefault()
	{
		_cameraYaw = 0f;
		_cameraPitch = 0.2f;
		_cameraDistance = DefaultDistance;
		_targetPosition = new Vector3(0f, 1.0f, 0f);
		UpdateCameraTransform();
	}

	private void UpdateCameraTransform()
	{
		if (_camera == null) return;
		_cameraPitch = Mathf.Clamp(_cameraPitch, -Mathf.Pi * 0.48f, Mathf.Pi * 0.48f);

		float cosPitch = Mathf.Cos(_cameraPitch);
		float sinPitch = Mathf.Sin(_cameraPitch);
		float cosYaw = Mathf.Cos(_cameraYaw);
		float sinYaw = Mathf.Sin(_cameraYaw);

		Vector3 offset = new Vector3(
			sinYaw * cosPitch,
			sinPitch,
			cosYaw * cosPitch
		) * _cameraDistance;

		Vector3 newPos = _targetPosition + offset;
		if (newPos.DistanceSquaredTo(_targetPosition) > 0.0001f)
		{
			Vector3 dir = (_targetPosition - newPos).Normalized();
			Vector3 up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
			_camera.LookAtFromPosition(newPos, _targetPosition, up);
		}
	}
}
