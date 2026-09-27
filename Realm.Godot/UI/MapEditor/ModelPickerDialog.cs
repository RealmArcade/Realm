using Godot;
using System;
using System.Collections.Generic;
using Realm.Godot.Utils;

public partial class ModelPickerDialog : FloatingDialogBase
{
	private SubViewportContainer _viewportContainer;
	private SubViewport _subViewport;
	private Camera3D _camera;
	private DirectionalLight3D _light;
	private Node3D _previewModelRoot;

	private LineEdit _txtModelPath;
	private Action<string> _setModelPathValue;
	private CheckBox _chkShowAllFolders;
	private Label _lblStatus;

	private string _entityId = "";
	private string _fieldName = "ModelPath";
	private string _domain = "units";
	private string _selectedModelPath = "";
	private string _initialModelPath = "";
	private bool _showAllFolders = false;
	private Action<string> _onApplied;

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

	public ModelPickerDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Model Asset Picker"), new Vector2(480, 560))
	{
		BuildControls();
	}

	private void BuildControls()
	{
		_viewportContainer = Add3DViewportContainer(BodyContainer, new Vector2(460, 240), out _subViewport, out _camera, out _light);
		_viewportContainer.GuiInput += OnViewportGuiInput;
		_viewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		var topControlsVBox = new VBoxContainer();
		topControlsVBox.AddThemeConstantOverride("separation", 6);
		BodyContainer.AddChild(topControlsVBox);

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

		topControlsVBox.AddChild(presetRow);

		AddLabel(topControlsVBox, TranslationServer.Translate("LMB: Orbit • RMB/MMB: Pan • Scroll: Zoom"), 10, UIStyle.ColorGoldDull);

		AddSectionHeader(BodyContainer, "📦 " + TranslationServer.Translate("MODEL ASSET SELECTION"), new Color(0.35f, 0.75f, 0.9f));

		_chkShowAllFolders = AddCheckBox(BodyContainer, TranslationServer.Translate("Show all model assets (all folders)"), _showAllFolders, (val) =>
		{
			_showAllFolders = val;
		}, "Include 3D models from all asset subdirectories");

		(_txtModelPath, _setModelPathValue) = AddAssetFilterDropdown(
			BodyContainer,
			TranslationServer.Translate("Model Asset (.rmesh):"),
			_selectedModelPath,
			(all) => ScanAvailableAssets("models", all || _showAllFolders, _domain),
			(val) =>
			{
				_selectedModelPath = val ?? string.Empty;
				LoadAndPreviewModel(_selectedModelPath);
			},
			TranslationServer.Translate("Select or search model asset..."),
			140f
		);

		_lblStatus = new Label();
		_lblStatus.AddThemeFontSizeOverride("font_size", 11);
		_lblStatus.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlowDim);
		_lblStatus.AutowrapMode = TextServer.AutowrapMode.Word;
		BodyContainer.AddChild(_lblStatus);
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

	public void OpenForEntity(string entityId, string fieldName, string domain, string currentPath, Action<string> onApplied = null)
	{
		_entityId = entityId ?? string.Empty;
		_fieldName = string.IsNullOrEmpty(fieldName) ? "ModelPath" : fieldName;
		_domain = string.IsNullOrEmpty(domain) ? "units" : domain;
		_selectedModelPath = currentPath ?? string.Empty;
		_initialModelPath = _selectedModelPath;
		_onApplied = onApplied;

		string fieldDisplay = _fieldName == "PortraitModelPath" ? "Portrait Model" : "Model Asset";
		TitleLabel.Text = $"{TranslationServer.Translate("Model Asset Picker")} - {_entityId} ({fieldDisplay})";

		_setModelPathValue?.Invoke(_selectedModelPath);
		ClearPreviewModel();

		OpenDialog();
		ResetCameraDefault();

		if (!string.IsNullOrWhiteSpace(_selectedModelPath))
		{
			LoadAndPreviewModel(_selectedModelPath);
		}
		else
		{
			var available = ScanAvailableAssets("models", false, _domain);
			if (available.Count > 0)
			{
				_selectedModelPath = available[0];
				_setModelPathValue?.Invoke(_selectedModelPath);
				LoadAndPreviewModel(_selectedModelPath);
			}
			else
			{
				if (_lblStatus != null) _lblStatus.Text = TranslationServer.Translate("No model currently selected.");
			}
		}
	}

	private void ClearPreviewModel()
	{
		if (_previewModelRoot != null && GodotObject.IsInstanceValid(_previewModelRoot))
		{
			_previewModelRoot.QueueFree();
			_previewModelRoot = null;
		}
	}

	private void LoadAndPreviewModel(string modelPath)
	{
		ClearPreviewModel();

		if (string.IsNullOrWhiteSpace(modelPath) || _subViewport == null)
		{
			if (_lblStatus != null) _lblStatus.Text = TranslationServer.Translate("No model path specified.");
			return;
		}

		Node loaded = ModelCache.GetModel(modelPath);
		if (loaded is Node3D node3D)
		{
			var cloned = (Node3D)node3D.Duplicate((int)Node.DuplicateFlags.UseInstantiation);
			cloned.Position = Vector3.Zero;
			cloned.Rotation = Vector3.Zero;
			cloned.Scale = Vector3.One;

			_subViewport.AddChild(cloned);
			_previewModelRoot = cloned;
			Realm.Godot.Animation.AnimationRetargetingService.TryApplyRiggedIdlePose(cloned, modelPath);
			if (_previewModelRoot.IsInsideTree())
			{
				_previewModelRoot.PropagateNotification((int)Node3D.NotificationTransformChanged);
			}

			FrameCameraOnModel(_previewModelRoot);

			string resolvedPath = ModelCache.ResolveModelPath(modelPath);
			string details = "";
			if (!string.IsNullOrEmpty(resolvedPath) && System.IO.File.Exists(resolvedPath))
			{
				bool teamCol = Realm.Shared.Metadata.RealmMetadataHelper.ExtractSupportsTeamColor(resolvedPath) == true;
				string? author = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAuthor(resolvedPath);
				if (teamCol) details += " • TeamColor: Yes";
				if (!string.IsNullOrEmpty(author)) details += $" • Author: {author}";
			}
			if (_lblStatus != null)
			{
				_lblStatus.Text = $"{TranslationServer.Translate("Loaded:")} {modelPath}{details}";
				_lblStatus.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
			}
		}
		else
		{
			if (_lblStatus != null)
			{
				_lblStatus.Text = $"{TranslationServer.Translate("Failed to load model:")} {modelPath}";
				_lblStatus.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f));
			}
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
			if (node is Node3D n3D)
			{
				currentTransform = parentTransform * n3D.Transform;
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

	protected override void OnApply()
	{
		if (!string.IsNullOrEmpty(_entityId))
		{
			Hud?.SaveEntityModelPathToMetadata(_entityId, _fieldName, _domain, _selectedModelPath);
			_onApplied?.Invoke(_selectedModelPath);
			Hud?.ShowFeedback(TranslationServer.Translate("Model asset updated successfully."));
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
