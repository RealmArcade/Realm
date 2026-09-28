using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Godot.Animation;
using Realm.Godot.Utils;
using Realm.Godot.Services;

public partial class AnimationPreviewDialog : FloatingDialogBase
{
	private static readonly string[] StandardActionTypes = new[]
	{
		"Idle",
		"Walk",
		"Attack",
		"Death",
		"Labor",
		"Spell_Cast",
		"Dance"
	};

	private static readonly Dictionary<string, string> ActionIcons = new()
	{
		{ "Idle", "💤" },
		{ "Walk", "🚶" },
		{ "Attack", "⚔️" },
		{ "Death", "💀" },
		{ "Labor", "⚒️" },
		{ "Spell_Cast", "🪄" },
		{ "Dance", "💃" }
	};

	private SubViewportContainer _viewportContainer;
	private SubViewport _subViewport;
	private Camera3D _camera;
	private DirectionalLight3D _light;
	private Node3D _previewModelRoot;
	private AnimationPlayer _animPlayer;

	private LineEdit _txtPreviewRanim;
	private Action<string> _setPreviewRanimValue;
	private OptionButton _optTargetAction;
	private VBoxContainer _actionListContainer;
	private VBoxContainer _configuredAttachmentsContainer;

	private readonly Dictionary<string, Node3D> _socketAnchorNodes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Node3D> _attachmentVisualNodes = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, bool> _attachmentVisibilities = new(StringComparer.OrdinalIgnoreCase);

	private Node _sourceSelectedObject;
	private string _currentUnitId = "";
	private string _currentPreviewRanim = "";
	private float _currentSpeed = 1.0f;
	private bool _isUpdatingUI;

	private Dictionary<string, List<GameHost.UnitAnimationEntry>> _workingAnimations = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, List<GameHost.UnitAnimationEntry>> _initialAnimations = new(StringComparer.OrdinalIgnoreCase);

	private Vector3 _modelCenter = Vector3.Zero;
	private Vector3 _targetPosition = Vector3.Zero;
	private float _defaultDistance = 3.0f;
	private float _cameraDistance = 3.0f;
	private float _defaultYaw = Mathf.DegToRad(30.0f);
	private float _defaultPitch = Mathf.DegToRad(15.0f);
	private float _cameraYaw = Mathf.DegToRad(30.0f);
	private float _cameraPitch = Mathf.DegToRad(15.0f);

	private bool _isOrbiting;
	private bool _isPanning;
	private Vector2 _lastMousePosition;

	public AnimationPreviewDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Unit Animation Studio"), new Vector2(500, 720))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		_viewportContainer = Add3DViewportContainer(BodyContainer, new Vector2(480, 220), out _subViewport, out _camera, out _light);
		_viewportContainer.GuiInput += OnViewportGuiInput;
		_viewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

		// CAMERA TOOLBAR
		var presetRow = new HBoxContainer();
		presetRow.AddThemeConstantOverride("separation", 4);

		var lblPreset = new Label();
		lblPreset.Text = TranslationServer.Translate("Camera:");
		lblPreset.AddThemeFontSizeOverride("font_size", 10);
		lblPreset.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		presetRow.AddChild(lblPreset);

		AddButton(presetRow, TranslationServer.Translate("Front"), () => SetCameraPreset(0f, 0f), "View model from front", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Side"), () => SetCameraPreset(90f, 0f), "View model from side", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Back"), () => SetCameraPreset(180f, 0f), "View model from back", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Iso"), () => SetCameraPreset(45f, 25f), "Isometric 3/4 view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("Top"), () => SetCameraPreset(0f, 85f), "Top-down view", 10, new Vector2(0, 22));
		AddButton(presetRow, TranslationServer.Translate("⟲ Reset"), () => ResetCameraDefault(), "Reset camera zoom and position to default", 10, new Vector2(0, 22));

		var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		presetRow.AddChild(spacer);

		AddButton(presetRow, "▶ " + TranslationServer.Translate("Play"), () => PlayCurrentPreview(), "Play Animation", 10, new Vector2(0, 22));
		AddButton(presetRow, "⏸ " + TranslationServer.Translate("Pause"), () => PauseAnimation(), "Pause Animation", 10, new Vector2(0, 22));
		AddButton(presetRow, "⏹ " + TranslationServer.Translate("Stop"), () => StopAnimation(), "Stop Animation", 10, new Vector2(0, 22));

		topControlsVBox.AddChild(presetRow);

		// TOP AUTO-COMPLETE DROPDOWN FOR PREVIEWING ANY .RANIM FILE
		var animInputSection = new VBoxContainer();
		animInputSection.AddThemeConstantOverride("separation", 4);

		AddSectionHeader(animInputSection, "🎬 " + TranslationServer.Translate("ANIMATION PREVIEW & ASSIGNMENT"), new Color(0.35f, 0.75f, 0.9f));

		(_txtPreviewRanim, _setPreviewRanimValue) = AddAssetFilterDropdown(
			animInputSection,
			TranslationServer.Translate("Preview .ranim:"),
			_currentPreviewRanim,
			(all) => ScanAvailableAssets("animations", all),
			(val) =>
			{
				_currentPreviewRanim = val ?? string.Empty;
				if (!string.IsNullOrWhiteSpace(_currentPreviewRanim))
				{
					PlayAnimationFile(_currentPreviewRanim);
				}
			},
			TranslationServer.Translate("Select or search .ranim asset from metadata..."),
			130f
		);

		var attHeaderRow = new HBoxContainer();
		attHeaderRow.AddThemeConstantOverride("separation", 6);

		var lblAttHeader = new Label();
		lblAttHeader.Text = "📎 " + TranslationServer.Translate("PREVIEW ATTACHMENTS & SOCKETS");
		lblAttHeader.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.4f));
		lblAttHeader.AddThemeFontSizeOverride("font_size", 11);
		lblAttHeader.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		attHeaderRow.AddChild(lblAttHeader);

		AddButton(attHeaderRow, "📎 " + TranslationServer.Translate("Sockets & VFX Studio..."), () => OpenFullSocketStudio(), "Open full Socket & VFX studio to configure attachments, ground auras, overhead effects, and non-hand sockets", 10, new Vector2(0, 22));
		animInputSection.AddChild(attHeaderRow);

		_configuredAttachmentsContainer = new VBoxContainer();
		_configuredAttachmentsContainer.AddThemeConstantOverride("separation", 4);
		_configuredAttachmentsContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		animInputSection.AddChild(_configuredAttachmentsContainer);

		// ADD TO ACTION ROW
		var addActionRow = new HBoxContainer();
		addActionRow.AddThemeConstantOverride("separation", 6);

		var lblAssign = new Label();
		lblAssign.Text = TranslationServer.Translate("Assign to Action:");
		lblAssign.CustomMinimumSize = new Vector2(130, 0);
		lblAssign.AddThemeFontSizeOverride("font_size", 11);
		addActionRow.AddChild(lblAssign);

		_optTargetAction = new OptionButton();
		_optTargetAction.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_optTargetAction.AddThemeFontSizeOverride("font_size", 11);
		for (int i = 0; i < StandardActionTypes.Length; i++)
		{
			string act = StandardActionTypes[i];
			string icon = ActionIcons.TryGetValue(act, out var ic) ? ic : "⚡";
			_optTargetAction.AddItem($"{icon} {act}", i);
		}
		addActionRow.AddChild(_optTargetAction);

		AddButton(addActionRow, "+ " + TranslationServer.Translate("Add to Action"), () => AddCurrentPreviewToSelectedAction(), "Add previewed animation into action's random array list", 11, new Vector2(120, 26));

		animInputSection.AddChild(addActionRow);
		topControlsVBox.AddChild(animInputSection);

		// ACTION TYPES & CONFIGURED ANIMATIONS LIST
		AddSectionHeader(BodyContainer, "📋 " + TranslationServer.Translate("CONFIGURED UNIT ANIMATIONS"), new Color(0.85f, 0.75f, 0.4f));

		var scrollBody = CreateScrollBody(250);
		_actionListContainer = new VBoxContainer();
		_actionListContainer.AddThemeConstantOverride("separation", 10);
		_actionListContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scrollBody.AddChild(_actionListContainer);
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
		_cameraDistance = Mathf.Clamp(_cameraDistance * factor, _defaultDistance * 0.15f, _defaultDistance * 6.0f);
		UpdateCameraTransform();
	}

	public void SetCameraPreset(float yawDegrees, float pitchDegrees)
	{
		_cameraYaw = Mathf.DegToRad(yawDegrees);
		_cameraPitch = Mathf.DegToRad(pitchDegrees);
		_targetPosition = _modelCenter;
		UpdateCameraTransform();
	}

	public void ResetCameraDefault()
	{
		_cameraDistance = _defaultDistance;
		_targetPosition = _modelCenter;
		_cameraYaw = _defaultYaw;
		_cameraPitch = _defaultPitch;
		UpdateCameraTransform();
	}

	private void UpdateCameraTransform()
	{
		if (_camera == null) return;

		_cameraPitch = Mathf.Clamp(_cameraPitch, -1.45f, 1.45f);

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
		_camera.Position = newPos;
		if (newPos.DistanceSquaredTo(_targetPosition) > 0.0001f)
		{
			Vector3 dir = (_targetPosition - newPos).Normalized();
			Vector3 up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
			_camera.LookAtFromPosition(newPos, _targetPosition, up);
		}
	}

	public void OpenForObject(Node selectedObject)
	{
		if (selectedObject == null || !GodotObject.IsInstanceValid(selectedObject)) return;

		_sourceSelectedObject = selectedObject;
		_currentUnitId = (selectedObject is Unit3D unit) ? unit.UnitId : "";

		Node modelRoot = selectedObject;
		if (selectedObject is Unit3D u && u.ModelNode != null)
		{
			modelRoot = u.ModelNode;
		}
		else if (selectedObject is Prop3D prop)
		{
			modelRoot = prop.GetNodeOrNull<Node3D>("VisualModel") ?? selectedObject;
		}

		var validation = SkeletonValidator.Validate(modelRoot);
		if (!validation.IsValid)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Selected model is not a compatible rigged mesh."));
			return;
		}

		TitleLabel.Text = $"{TranslationServer.Translate("Unit Animation Studio")} - {selectedObject.Name}";

		InitWorkingAnimations();
		ClearPreviewModel();
		OpenDialog();
		ResetCameraDefault();

		SetupPreviewModel(modelRoot);
		RebuildConfiguredAttachmentsUI();

		SelectFirstAvailableOrAssignedAnimation();
		RebuildActionListUI();
	}

	public void OpenForUnitId(string unitId, string modelPath = null)
	{
		_currentUnitId = unitId;
		TitleLabel.Text = $"{TranslationServer.Translate("Unit Animation Studio")} - {unitId}";

		if (string.IsNullOrEmpty(modelPath) && !string.IsNullOrEmpty(unitId))
		{
			if (GameHost.UnitRegistry.TryGetValue(unitId, out var uMeta) && !string.IsNullOrEmpty(uMeta.ModelPath))
			{
				modelPath = uMeta.ModelPath;
			}
			else
			{
				try
				{
					string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
					var metadata = MetadataService.Instance.LoadMetadata(wsPath);
					var unit = metadata.GetUnit(unitId);
					if (unit.HasValue && !string.IsNullOrEmpty(unit.Value.ModelPath))
					{
						modelPath = unit.Value.ModelPath;
					}
				}
				catch { }
			}
		}

		InitWorkingAnimations();
		ClearPreviewModel();

		OpenDialog();
		ResetCameraDefault();

		Node3D loadedModel = null;
		if (!string.IsNullOrEmpty(modelPath))
		{
			var loaded = ModelCache.GetModel(modelPath);
			if (loaded is Node3D node3D) loadedModel = node3D;
		}

		if (loadedModel != null)
		{
			SetupPreviewModel(loadedModel);
		}
		RebuildConfiguredAttachmentsUI();

		SelectFirstAvailableOrAssignedAnimation();
		RebuildActionListUI();
	}

	private void InitWorkingAnimations()
	{
		_workingAnimations.Clear();
		_initialAnimations.Clear();

		if (!string.IsNullOrEmpty(_currentUnitId) && GameHost.UnitRegistry.TryGetValue(_currentUnitId, out var uMeta) && uMeta.Animations != null)
		{
			foreach (var kvp in uMeta.Animations)
			{
				var list = kvp.Value != null
					? new List<GameHost.UnitAnimationEntry>(kvp.Value)
					: new List<GameHost.UnitAnimationEntry>();
				_workingAnimations[kvp.Key] = list;
				_initialAnimations[kvp.Key] = new List<GameHost.UnitAnimationEntry>(list);
			}
		}

		if (_workingAnimations.Count == 0 && !string.IsNullOrEmpty(_currentUnitId))
		{
			try
			{
				string wsPath = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
				var metadata = MetadataService.Instance.LoadMetadata(wsPath);
				var unit = metadata.GetUnit(_currentUnitId);
				if (unit.HasValue && unit.Value.Animations != null)
				{
					foreach (var kvp in unit.Value.Animations)
					{
						var list = kvp.Value != null
							? new List<GameHost.UnitAnimationEntry>(kvp.Value)
							: new List<GameHost.UnitAnimationEntry>();
						_workingAnimations[kvp.Key] = list;
						_initialAnimations[kvp.Key] = new List<GameHost.UnitAnimationEntry>(list);
					}
				}
			}
			catch { }
		}
	}

	private void RebuildConfiguredAttachmentsUI()
	{
		if (_configuredAttachmentsContainer == null) return;

		foreach (Node child in _configuredAttachmentsContainer.GetChildren())
		{
			child.QueueFree();
		}

		var configured = string.IsNullOrEmpty(_currentUnitId)
			? new List<ObjectAttachmentDialog.ConfiguredAttachmentEntry>()
			: ObjectAttachmentDialog.GetConfiguredAttachmentsForObject(_currentUnitId);

		if (configured.Count == 0)
		{
			var emptyLabel = new Label
			{
				Text = TranslationServer.Translate("No attachments configured on this unit.")
			};
			emptyLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			emptyLabel.AddThemeFontSizeOverride("font_size", 11);
			_configuredAttachmentsContainer.AddChild(emptyLabel);
			return;
		}

		for (int i = 0; i < configured.Count; i++)
		{
			var entry = configured[i];
			string normSocket = ObjectAttachmentDialog.NormalizeSocketId(entry.SocketId);
			string key = ObjectAttachmentDialog.GetAttachmentKey(normSocket, entry.AttachmentId, entry.Index, entry.Orientation.ParentAttachmentId);

			bool isVisible = !_attachmentVisibilities.TryGetValue(key, out bool vis) || vis;

			var card = new PanelContainer();
			card.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

			var row = new HBoxContainer();
			row.AddThemeConstantOverride("separation", 6);

			var btnEye = new Button();
			btnEye.Set("icon_max_width", 0);
			btnEye.Text = isVisible ? "👁️" : "🚫";
			btnEye.TooltipText = TranslationServer.Translate("Toggle attachment preview visibility");
			btnEye.CustomMinimumSize = new Vector2(28, 22);
			btnEye.FocusMode = Control.FocusModeEnum.None;

			string capturedKey = key;
			btnEye.Pressed += () =>
			{
				bool curVis = !_attachmentVisibilities.TryGetValue(capturedKey, out bool v) || v;
				bool newVis = !curVis;
				_attachmentVisibilities[capturedKey] = newVis;
				btnEye.Text = newVis ? "👁️" : "🚫";

				if (_attachmentVisualNodes.TryGetValue(capturedKey, out var visualNode) && GodotObject.IsInstanceValid(visualNode))
				{
					visualNode.Visible = newVis;
				}
			};
			row.AddChild(btnEye);

			string parentAttId = entry.Orientation.ParentAttachmentId;
			bool isChild = !string.IsNullOrEmpty(parentAttId);
			string badgeText = isChild
				? $"[{entry.SocketId} ➔ {System.IO.Path.GetFileNameWithoutExtension(parentAttId)}]"
				: $"[{entry.SocketId}]";

			var badge = new Label
			{
				Text = badgeText,
				CustomMinimumSize = new Vector2(isChild ? 110 : 75, 0),
				ClipText = true
			};
			badge.AddThemeColorOverride("font_color", isChild ? new Color(0.9f, 0.6f, 1.0f) : UIStyle.ColorCyanGlow);
			badge.AddThemeFontSizeOverride("font_size", 10);
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

			string capturedSocket = entry.SocketId;
			string capturedAtt = entry.AttachmentId;
			var btnEdit = new Button();
			btnEdit.Set("icon_max_width", 0);
			btnEdit.Text = "✏️";
			btnEdit.TooltipText = TranslationServer.Translate("Edit in Socket & VFX Studio");
			btnEdit.CustomMinimumSize = new Vector2(28, 22);
			btnEdit.FocusMode = Control.FocusModeEnum.None;
			btnEdit.Pressed += () =>
			{
				Hud?.OpenObjectAttachmentDialog(
					_currentUnitId,
					capturedAtt,
					capturedSocket,
					_previewModelRoot,
					(orientation) =>
					{
						SetupSocketAnchors();
						MountAllConfiguredAttachments();
						RebuildConfiguredAttachmentsUI();
					}
				);
			};
			row.AddChild(btnEdit);

			card.AddChild(row);
			_configuredAttachmentsContainer.AddChild(card);
		}
	}

	private void OpenFullSocketStudio()
	{
		Hud?.OpenObjectAttachmentDialog(
			_currentUnitId,
			null,
			"RightHand",
			_previewModelRoot,
			(orientation) =>
			{
				SetupSocketAnchors();
				MountAllConfiguredAttachments();
				RebuildConfiguredAttachmentsUI();
			}
		);
	}

	private void SelectFirstAvailableOrAssignedAnimation()
	{
		foreach (var act in StandardActionTypes)
		{
			if (_workingAnimations.TryGetValue(act, out var list) && list.Count > 0)
			{
				var entry = list[0];
				_currentPreviewRanim = entry.Animation;
				_setPreviewRanimValue?.Invoke(_currentPreviewRanim);
				PlayAnimationFile(_currentPreviewRanim);
				return;
			}
		}

		var allAvailable = ScanAvailableAssets("animations");
		if (allAvailable.Count > 0)
		{
			_currentPreviewRanim = allAvailable[0];
			_setPreviewRanimValue?.Invoke(_currentPreviewRanim);
			PlayAnimationFile(_currentPreviewRanim);
		}
	}

	private void RebuildActionListUI()
	{
		if (_actionListContainer == null) return;

		foreach (Node child in _actionListContainer.GetChildren())
		{
			child.QueueFree();
		}

		foreach (string actionType in StandardActionTypes)
		{
			var actionCard = new PanelContainer();
			actionCard.AddThemeStyleboxOverride("panel", UIStyle.CreateLightInnerPanel());

			var cardVBox = new VBoxContainer();
			cardVBox.AddThemeConstantOverride("separation", 6);
			actionCard.AddChild(cardVBox);

			var headerRow = new HBoxContainer();
			headerRow.AddThemeConstantOverride("separation", 6);

			string icon = ActionIcons.TryGetValue(actionType, out var ic) ? ic : "⚡";
			var lblHeader = new Label();
			lblHeader.Text = $"{icon} {actionType}";
			lblHeader.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			lblHeader.AddThemeFontSizeOverride("font_size", 12);
			lblHeader.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
			headerRow.AddChild(lblHeader);

			_workingAnimations.TryGetValue(actionType, out var animList);
			int count = animList?.Count ?? 0;
			var countBadge = new Label();
			countBadge.Text = count == 1 ? "1 anim" : $"{count} anims";
			countBadge.AddThemeColorOverride("font_color", count > 0 ? UIStyle.ColorCyanGlow : UIStyle.ColorGoldDull);
			countBadge.AddThemeFontSizeOverride("font_size", 10);
			headerRow.AddChild(countBadge);

			cardVBox.AddChild(headerRow);

			if (animList == null || animList.Count == 0)
			{
				var fallbackLbl = new Label();
				fallbackLbl.Text = TranslationServer.Translate($"[Default fallback: {actionType.ToLowerInvariant()}.ranim]");
				fallbackLbl.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.65f, 0.7f));
				fallbackLbl.AddThemeFontSizeOverride("font_size", 11);
				cardVBox.AddChild(fallbackLbl);
			}
			else
			{
				for (int i = 0; i < animList.Count; i++)
				{
					int index = i;
					var entry = animList[i];
					string animFile = entry.Animation;

					var itemRow = new HBoxContainer();
					itemRow.AddThemeConstantOverride("separation", 6);

					var badge = new Label();
					badge.Text = $"{actionType}_{index}";
					badge.CustomMinimumSize = new Vector2(55, 0);
					badge.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
					badge.AddThemeFontSizeOverride("font_size", 10);
					itemRow.AddChild(badge);

					var nameLbl = new Label();
					nameLbl.Text = animFile;
					nameLbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
					nameLbl.AddThemeFontSizeOverride("font_size", 11);
					nameLbl.ClipText = true;
					itemRow.AddChild(nameLbl);

					AddButton(itemRow, "▶ " + TranslationServer.Translate("Preview"), () =>
					{
						_currentPreviewRanim = entry.Animation;
						_setPreviewRanimValue?.Invoke(entry.Animation);

						for (int a = 0; a < StandardActionTypes.Length; a++)
						{
							if (StandardActionTypes[a].Equals(actionType, StringComparison.OrdinalIgnoreCase))
							{
								_optTargetAction.Selected = a;
								break;
							}
						}

						PlayAnimationFile(entry.Animation);
					}, "Preview this animation", 10, new Vector2(65, 22));

					AddButton(itemRow, "✕ " + TranslationServer.Translate("Remove"), () =>
					{
						RemoveAnimationFromAction(actionType, index);
					}, "Remove this animation from action array", 10, new Vector2(65, 22));

					cardVBox.AddChild(itemRow);
				}
			}

			_actionListContainer.AddChild(actionCard);
		}
	}

	private void AddCurrentPreviewToSelectedAction()
	{
		string animFile = _currentPreviewRanim?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(animFile))
		{
			Hud?.ShowFeedback(TranslationServer.Translate("Please select an animation in the preview field first."));
			return;
		}

		int selectedActionIdx = _optTargetAction != null ? _optTargetAction.Selected : 0;
		if (selectedActionIdx < 0 || selectedActionIdx >= StandardActionTypes.Length) selectedActionIdx = 0;
		string actionType = StandardActionTypes[selectedActionIdx];

		if (!_workingAnimations.TryGetValue(actionType, out var list) || list == null)
		{
			list = new List<GameHost.UnitAnimationEntry>();
			_workingAnimations[actionType] = list;
		}

		bool exists = list.Any(e => e.Animation.Equals(animFile, StringComparison.OrdinalIgnoreCase));

		if (exists)
		{
			Hud?.ShowFeedback(TranslationServer.Translate("This animation already exists in this action."));
			return;
		}

		list.Add(new GameHost.UnitAnimationEntry
		{
			Animation = animFile
		});

		Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Added {0} to {1}"), animFile, actionType));
		RebuildActionListUI();
	}

	private void RemoveAnimationFromAction(string actionType, int index)
	{
		if (_workingAnimations.TryGetValue(actionType, out var list) && list != null && index >= 0 && index < list.Count)
		{
			var removed = list[index];
			list.RemoveAt(index);
			if (list.Count == 0)
			{
				_workingAnimations.Remove(actionType);
			}
			Hud?.ShowFeedback(string.Format(TranslationServer.Translate("Removed {0} from {1}"), removed.Animation, actionType));
			RebuildActionListUI();
		}
	}

	private void ClearPreviewModel()
	{
		ClearAttachmentVisuals();
		_socketAnchorNodes.Clear();
		if (_previewModelRoot != null && GodotObject.IsInstanceValid(_previewModelRoot))
		{
			if (_animPlayer != null && GodotObject.IsInstanceValid(_animPlayer))
			{
				_animPlayer.Stop(true);
			}
			_previewModelRoot.QueueFree();
			_previewModelRoot = null;
			_animPlayer = null;
		}
	}

	private void ClearAttachmentVisuals()
	{
		foreach (var kvp in _attachmentVisualNodes)
		{
			if (kvp.Value != null && GodotObject.IsInstanceValid(kvp.Value))
			{
				kvp.Value.GetParent()?.RemoveChild(kvp.Value);
				kvp.Value.QueueFree();
			}
		}
		_attachmentVisualNodes.Clear();
	}

	private void SetupPreviewModel(Node sourceModelRoot)
	{
		if (sourceModelRoot == null || _subViewport == null) return;

		var clonedNode = (Node3D)sourceModelRoot.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
		if (clonedNode == null) return;

		RemoveAllBoneAttachments(clonedNode);

		clonedNode.Position = Vector3.Zero;
		clonedNode.Rotation = Vector3.Zero;
		clonedNode.Scale = Vector3.One;

		_subViewport.AddChild(clonedNode);
		_previewModelRoot = clonedNode;

		if (_previewModelRoot.IsInsideTree())
		{
			_previewModelRoot.PropagateNotification((int)Node3D.NotificationTransformChanged);
		}

		FrameCameraOnModel(_previewModelRoot);

		SetupSocketAnchors();
		MountAllConfiguredAttachments();

		_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
	}

	private void SetupSocketAnchors()
	{
		_socketAnchorNodes.Clear();
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;

		Aabb modelAabb = CalculatePreviewModelAabb(_previewModelRoot);
		var skeleton = SkeletonValidator.FindSkeleton(_previewModelRoot);

		if (skeleton != null)
		{
			AddPseudoAnchor("Ground", new Vector3(0, modelAabb.Position.Y, 0));
			AddPseudoAnchor("Center", new Vector3(0, modelAabb.GetCenter().Y, 0));
			AddPseudoAnchor("Overhead", new Vector3(0, modelAabb.End.Y + 0.3f, 0));
			AddPseudoAnchor("Pivot", Vector3.Zero);

			AddBoneAnchor(skeleton, HumanoidBone.RightHand, "RightHand");
			AddBoneAnchor(skeleton, HumanoidBone.LeftHand, "LeftHand");
			AddBoneAnchor(skeleton, HumanoidBone.Chest, "Chest");
			AddBoneAnchor(skeleton, HumanoidBone.Hips, "Hips");
			AddBoneAnchor(skeleton, HumanoidBone.Head, "Head");
			AddBoneAnchor(skeleton, HumanoidBone.LeftFoot, "LeftFoot");
			AddBoneAnchor(skeleton, HumanoidBone.RightFoot, "RightFoot");
		}
		else
		{
			AddPseudoAnchor("Center", modelAabb.GetCenter());
			AddPseudoAnchor("Top", new Vector3(modelAabb.GetCenter().X, modelAabb.End.Y, modelAabb.GetCenter().Z));
			AddPseudoAnchor("Base", new Vector3(modelAabb.GetCenter().X, modelAabb.Position.Y, modelAabb.GetCenter().Z));
			AddPseudoAnchor("Pivot", Vector3.Zero);
		}
	}

	private void AddPseudoAnchor(string socketId, Vector3 pos)
	{
		string normSocket = ObjectAttachmentDialog.NormalizeSocketId(socketId);
		var anchor = new Node3D { Name = $"PreviewSocketAnchor_{normSocket}" };
		anchor.Position = pos;
		_previewModelRoot.AddChild(anchor);
		_socketAnchorNodes[normSocket] = anchor;
	}

	private void AddBoneAnchor(Skeleton3D skeleton, HumanoidBone bone, string socketId)
	{
		string normSocket = ObjectAttachmentDialog.NormalizeSocketId(socketId);
		int boneIdx = skeleton.FindBoneInSkeleton(bone);
		if (boneIdx >= 0)
		{
			var ba = new BoneAttachment3D
			{
				Name = $"PreviewBoneAttachment_{normSocket}",
				BoneName = skeleton.GetBoneName(boneIdx),
				BoneIdx = boneIdx
			};
			skeleton.AddChild(ba);
			_socketAnchorNodes[normSocket] = ba;
		}
	}

	private void MountAllConfiguredAttachments()
	{
		ClearAttachmentVisuals();
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;
		if (string.IsNullOrEmpty(_currentUnitId)) return;

		var configured = ObjectAttachmentDialog.GetConfiguredAttachmentsForObject(_currentUnitId);

		foreach (var entry in configured)
		{
			if (string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				MountAttachmentVisual(entry);
			}
		}

		foreach (var entry in configured)
		{
			if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				MountAttachmentVisual(entry);
			}
		}
	}

	private void MountAttachmentVisual(ObjectAttachmentDialog.ConfiguredAttachmentEntry entry)
	{
		string normSocket = ObjectAttachmentDialog.NormalizeSocketId(entry.SocketId);
		if (!_socketAnchorNodes.TryGetValue(normSocket, out var targetAnchor) || targetAnchor == null || !GodotObject.IsInstanceValid(targetAnchor))
		{
			return;
		}

		string key = ObjectAttachmentDialog.GetAttachmentKey(normSocket, entry.AttachmentId, entry.Index, entry.Orientation.ParentAttachmentId);

		if (string.IsNullOrEmpty(entry.AttachmentId) || entry.AttachmentId.Equals("null", StringComparison.OrdinalIgnoreCase) || entry.AttachmentId.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		Node3D loaded = Unit3D.ResolveAndInstantiateAttachment(entry.AttachmentId, out _, out _, out _);
		if (loaded != null)
		{
			string cleanAttId = entry.AttachmentId.StartsWith("vfx:", StringComparison.OrdinalIgnoreCase)
				? entry.AttachmentId
				: System.IO.Path.GetFileNameWithoutExtension(entry.AttachmentId);

			loaded.Name = $"AttVisual_{key}";
			loaded.SetMeta("AttachmentId", entry.AttachmentId);
			loaded.SetMeta("CleanAttachmentId", cleanAttId);

			loaded.Position = entry.Orientation.Position + (loaded.Transform.Basis.Y * entry.Orientation.NormalOffset);
			loaded.RotationDegrees = entry.Orientation.RotationDegrees;
			loaded.Scale = entry.Orientation.ScaleVector == Vector3.Zero
				? Vector3.One * (entry.Orientation.Scale <= 0f ? 1.0f : entry.Orientation.Scale)
				: entry.Orientation.ScaleVector;

			bool isVisible = !_attachmentVisibilities.TryGetValue(key, out bool vis) || vis;
			loaded.Visible = isVisible;

			Node3D attachTarget = targetAnchor;
			if (!string.IsNullOrEmpty(entry.Orientation.ParentAttachmentId))
			{
				var parentMesh = Unit3D.FindAttachmentInNode(targetAnchor, entry.Orientation.ParentAttachmentId)
					?? (_previewModelRoot != null ? Unit3D.FindAttachmentInNode(_previewModelRoot, entry.Orientation.ParentAttachmentId) : null);
				if (parentMesh != null)
				{
					attachTarget = parentMesh;
				}
			}

			attachTarget.AddChild(loaded);
			_attachmentVisualNodes[key] = loaded;
		}
	}

	private static Aabb CalculatePreviewModelAabb(Node3D root)
	{
		Aabb combinedAabb = new Aabb();
		bool hasAabb = false;

		void Collect(Node current)
		{
			if (current is MeshInstance3D meshInst && meshInst.Mesh != null && meshInst.Visible)
			{
				Transform3D relXform = root.GlobalTransform.AffineInverse() * meshInst.GlobalTransform;
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
			foreach (Node child in current.GetChildren())
			{
				if (child is not BoneAttachment3D && !child.Name.ToString().StartsWith("PreviewSocketAnchor_") && !child.Name.ToString().StartsWith("AttVisual_"))
				{
					Collect(child);
				}
			}
		}

		Collect(root);
		if (!hasAabb)
		{
			combinedAabb = new Aabb(new Vector3(-0.5f, 0f, -0.5f), new Vector3(1.0f, 1.8f, 1.0f));
		}
		return combinedAabb;
	}

	private static void RemoveAllBoneAttachments(Node node)
	{
		if (node == null) return;
		var boneAttachments = new List<BoneAttachment3D>();
		CollectBoneAttachmentsRecursive(node, boneAttachments);
		foreach (var ba in boneAttachments)
		{
			ba.GetParent()?.RemoveChild(ba);
			ba.QueueFree();
		}
	}

	private static void CollectBoneAttachmentsRecursive(Node node, List<BoneAttachment3D> list)
	{
		if (node is BoneAttachment3D ba)
		{
			list.Add(ba);
			return;
		}
		foreach (Node child in node.GetChildren())
		{
			CollectBoneAttachmentsRecursive(child, list);
		}
	}

	private void FrameCameraOnModel(Node3D modelRoot)
	{
		if (modelRoot == null || _camera == null) return;

		Aabb totalAabb = new Aabb();
		bool hasMesh = false;

		Action<Node, Transform3D> collectAabb = null;
		collectAabb = (node, parentTransform) =>
		{
			Transform3D currentTransform = parentTransform;
			if (node is Node3D node3D)
			{
				currentTransform = parentTransform * node3D.Transform;
			}

			if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
			{
				Aabb localAabb = meshInstance.GetAabb();
				Vector3 min = localAabb.Position;
				Vector3 max = localAabb.End;
				Vector3[] corners = new Vector3[]
				{
					currentTransform * new Vector3(min.X, min.Y, min.Z),
					currentTransform * new Vector3(max.X, min.Y, min.Z),
					currentTransform * new Vector3(min.X, max.Y, min.Z),
					currentTransform * new Vector3(max.X, max.Y, min.Z),
					currentTransform * new Vector3(min.X, min.Y, max.Z),
					currentTransform * new Vector3(max.X, min.Y, max.Z),
					currentTransform * new Vector3(min.X, max.Y, max.Z),
					currentTransform * max
				};

				Aabb globalMeshAabb = new Aabb(corners[0], Vector3.Zero);
				foreach (var c in corners) globalMeshAabb = globalMeshAabb.Expand(c);

				if (!hasMesh)
				{
					totalAabb = globalMeshAabb;
					hasMesh = true;
				}
				else
				{
					totalAabb = totalAabb.Merge(globalMeshAabb);
				}
			}

			int childCount = node.GetChildCount();
			for (int i = 0; i < childCount; i++)
			{
				collectAabb(node.GetChild(i), currentTransform);
			}
		};

		collectAabb(modelRoot, Transform3D.Identity);

		if (hasMesh && totalAabb.Size.LengthSquared() > 0.001f)
		{
			_modelCenter = totalAabb.GetCenter();
			float radius = totalAabb.Size.Length() * 0.6f;
			_defaultDistance = radius * 2.2f;
		}
		else
		{
			_modelCenter = new Vector3(0, 0.8f, 0);
			_defaultDistance = 2.8f;
		}

		_defaultYaw = Mathf.DegToRad(30.0f);
		_defaultPitch = Mathf.DegToRad(15.0f);
		ResetCameraDefault();
	}

	private void PlayCurrentPreview()
	{
		if (!string.IsNullOrEmpty(_currentPreviewRanim))
		{
			PlayAnimationFile(_currentPreviewRanim);
		}
	}

	private void PauseAnimation()
	{
		if (_animPlayer != null && _animPlayer.IsPlaying())
		{
			_animPlayer.Pause();
		}
	}

	private void StopAnimation()
	{
		if (_animPlayer != null)
		{
			_animPlayer.Stop(true);
		}
	}

	public void PlayAnimationFile(string animFileName)
	{
		if (string.IsNullOrEmpty(animFileName)) return;
		if (_previewModelRoot == null || !GodotObject.IsInstanceValid(_previewModelRoot)) return;

		_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
		if (_animPlayer == null) return;

		string animName = System.IO.Path.GetFileNameWithoutExtension(animFileName);

		if (_animPlayer.HasAnimation(animName))
		{
			var anim = _animPlayer.GetAnimation(animName);
			if (anim != null) anim.LoopMode = Godot.Animation.LoopModeEnum.Linear;
			_animPlayer.SpeedScale = _currentSpeed;
			_animPlayer.Play(animName);
			return;
		}

		string filePath = AnimationRetargetingService.ResolveAnimationFilePath(animFileName, _currentUnitId);
		RealmAnimationData animData = null;
		if (!string.IsNullOrEmpty(filePath))
		{
			animData = AnimationRetargetingService.GetOrLoadRanimData(filePath);
		}
		else
		{
			animData = animName switch
			{
				"Idle" => RealmDefaultAnimations.Idle,
				"Walk" => RealmDefaultAnimations.Walk,
				"Attack" => RealmDefaultAnimations.Attack,
				"Death" => RealmDefaultAnimations.Death,
				"Labor" => RealmDefaultAnimations.Labor,
				"Spell_Cast" => RealmDefaultAnimations.Spell_Cast,
				"Dance" => RealmDefaultAnimations.Dance,
				_ => null
			};
		}

		if (animData != null)
		{
			if (AnimationRetargetingService.RetargetAndBind(animData, _previewModelRoot, animName, out _))
			{
				_animPlayer = AnimationRetargetingService.FindOrCreateAnimationPlayer(_previewModelRoot);
				if (_animPlayer != null && _animPlayer.HasAnimation(animName))
				{
					var anim = _animPlayer.GetAnimation(animName);
					if (anim != null) anim.LoopMode = Godot.Animation.LoopModeEnum.Linear;
					_animPlayer.SpeedScale = _currentSpeed;
					_animPlayer.Play(animName);
				}
			}
		}
	}

	protected override void OnApply()
	{
		if (!string.IsNullOrEmpty(_currentUnitId))
		{
			Hud?.SaveCustomUnitAnimations(_currentUnitId, _workingAnimations);
			Hud?.ShowFeedback(TranslationServer.Translate("Unit animations applied successfully."));
		}

		ClearPreviewModel();
	}

	protected override void OnCancel()
	{
		ClearPreviewModel();
	}

	public override void CloseDialog()
	{
		ClearPreviewModel();
		base.CloseDialog();
	}
}
