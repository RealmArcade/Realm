using System;
using Godot;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class FloatingPreview3DDialogBase : FloatingDialogBase
{
	public FloatingPreview3DDialogBase() { }

	protected SubViewportContainer PreviewViewportContainer;
	protected SubViewport PreviewSubViewport;
	protected Camera3D PreviewCamera;
	protected DirectionalLight3D PreviewLight;
	protected Node3D PreviewSceneRoot;

	protected float DefaultDistance = 5.0f;
	protected float CameraDistance = 5.0f;
	protected float DefaultYaw = Mathf.DegToRad(45.0f);
	protected float CameraYaw = Mathf.DegToRad(45.0f);
	protected float DefaultPitch = Mathf.DegToRad(25.0f);
	protected float CameraPitch = Mathf.DegToRad(25.0f);
	protected Vector3 DefaultTargetPosition = Vector3.Zero;
	protected Vector3 TargetPosition = Vector3.Zero;

	protected bool HasUserAdjustedCamera;
	protected Aabb FramedBounds;

	protected bool IsOrbiting;
	protected bool IsPanning;
	protected Vector2 LastMousePosition;

	protected HBoxContainer CameraPresetToolbar;

	public SubViewport SubViewport => PreviewSubViewport;
	public Camera3D Camera => PreviewCamera;
	public DirectionalLight3D Light => PreviewLight;

	public FloatingPreview3DDialogBase(MapEditorHUD hud, string titleText, Vector2 minSize)
		: base(hud, titleText, minSize)
	{
	}

	public virtual SubViewportContainer Add3DPreviewViewport(Control parent, Vector2 minSize)
	{
		PreviewViewportContainer = Add3DViewportContainer(parent, minSize, out PreviewSubViewport, out PreviewCamera, out PreviewLight);
		PreviewViewportContainer.GuiInput += OnViewportGuiInput;
		PreviewSubViewport.SizeChanged += OnPreviewViewportSizeChanged;
		PreviewViewportContainer.MouseDefaultCursorShape = CursorShape.Cross;

		PreviewSceneRoot = new Node3D { Name = "PreviewRoot" };
		PreviewSubViewport.AddChild(PreviewSceneRoot);

		UpdateCameraTransform();
		return PreviewViewportContainer;
	}

	public virtual HBoxContainer AddCameraPresetToolbar(
		Control parent,
		bool includeBack = true,
		bool includeLightingToggle = false,
		bool includeGridToggle = false,
		Action onToggleGrid = null)
	{
		CameraPresetToolbar = new HBoxContainer();
		CameraPresetToolbar.Name = "CameraPresetToolbar_HBoxContainer";
		CameraPresetToolbar.AddThemeConstantOverride("separation", 4);

		var lblPreset = new Label();
		lblPreset.Name = "LblPreset_Label";
		lblPreset.Text = TranslationServer.Translate("Camera:");
		lblPreset.AddThemeFontSizeOverride("font_size", 10);
		lblPreset.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		CameraPresetToolbar.AddChild(lblPreset);

		AddButton(CameraPresetToolbar, TranslationServer.Translate("Front"), () => SetCameraPreset(0f, 0f), "View front", 10, new Vector2(0, 22));
		AddButton(CameraPresetToolbar, TranslationServer.Translate("Side"), () => SetCameraPreset(90f, 0f), "View side", 10, new Vector2(0, 22));
		if (includeBack)
		{
			AddButton(CameraPresetToolbar, TranslationServer.Translate("Back"), () => SetCameraPreset(180f, 0f), "View back", 10, new Vector2(0, 22));
		}

		AddButton(CameraPresetToolbar, TranslationServer.Translate("Iso"), () => SetCameraPreset(45f, 25f), "Isometric view", 10, new Vector2(0, 22));
		AddButton(CameraPresetToolbar, TranslationServer.Translate("Top"), () => SetCameraPreset(0f, 85f), "Top-down view", 10, new Vector2(0, 22));
		AddButton(CameraPresetToolbar, TranslationServer.Translate("⟲ Reset"), () => ResetCameraDefault(), "Reset camera zoom and position", 10, new Vector2(0, 22));

		if (includeLightingToggle)
		{
			AddButton(CameraPresetToolbar, "☀️", () => TogglePreviewLight(), "Toggle light angle", 10, new Vector2(26, 22));
		}

		if (includeGridToggle && onToggleGrid != null)
		{
			var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
			CameraPresetToolbar.AddChild(spacer);
			AddButton(CameraPresetToolbar, TranslationServer.Translate("Toggle Grid"), onToggleGrid, "Toggle preview ground plane", 10, new Vector2(0, 22));
		}

		parent.AddChild(CameraPresetToolbar);
		return CameraPresetToolbar;
	}

	public virtual void SetCameraPreset(float yawDegrees, float pitchDegrees)
	{
		CameraYaw = Mathf.DegToRad(yawDegrees);
		CameraPitch = Mathf.DegToRad(pitchDegrees);
		UpdateCameraTransform();
	}

	public virtual void ResetCameraDefault()
	{
		CameraDistance = DefaultDistance;
		CameraYaw = DefaultYaw;
		CameraPitch = DefaultPitch;
		TargetPosition = DefaultTargetPosition;
		HasUserAdjustedCamera = false;
		UpdateCameraTransform();
	}

	public virtual void ZoomCamera(float direction)
	{
		HasUserAdjustedCamera = true;
		float factor = direction > 0 ? 1.15f : 0.85f;
		CameraDistance = Mathf.Clamp(CameraDistance * factor, Math.Max(0.5f, DefaultDistance * 0.15f), DefaultDistance * 6.0f);
		UpdateCameraTransform();
	}

	public virtual void UpdateCameraTransform()
	{
		if (PreviewCamera == null || !GodotObject.IsInstanceValid(PreviewCamera)) return;

		CameraPitch = Mathf.Clamp(CameraPitch, -1.45f, 1.45f);

		Vector3 offset = ModelPreviewFraming.ComputeCameraOffset(CameraDistance, CameraYaw, CameraPitch);
		Vector3 newPos = TargetPosition + offset;
		PreviewCamera.Position = newPos;

		if (newPos.DistanceSquaredTo(TargetPosition) > 0.0001f)
		{
			Vector3 dir = (TargetPosition - newPos).Normalized();
			Vector3 up = Mathf.Abs(dir.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
			PreviewCamera.LookAtFromPosition(newPos, TargetPosition, up);
		}
	}

	public virtual void FrameCameraOnNode(Node3D rootNode)
	{
		if (rootNode == null || PreviewCamera == null || !GodotObject.IsInstanceValid(PreviewCamera)) return;

		Aabb modelBounds = ModelPreviewFraming.ComputeVisibleBounds(rootNode);

		if (ModelPreviewFraming.HasUsableBounds(modelBounds))
		{
			FramedBounds = modelBounds;
			TargetPosition = ModelPreviewFraming.ComputeFramedCenter(modelBounds);
			DefaultTargetPosition = TargetPosition;
			DefaultDistance = ComputeFramedDistance();
		}
		else
		{
			FramedBounds = new Aabb(new Vector3(-0.5f, 0.0f, -0.5f), Vector3.One);
			TargetPosition = new Vector3(0.0f, 0.8f, 0.0f);
			DefaultTargetPosition = TargetPosition;
			DefaultDistance = ModelPreviewFraming.DefaultFallbackDistance;
		}

		CameraDistance = DefaultDistance;
		HasUserAdjustedCamera = false;
		UpdateCameraTransform();
	}

	protected float ComputeFramedDistance()
	{
		float verticalFovDegrees = PreviewCamera != null && GodotObject.IsInstanceValid(PreviewCamera) ? PreviewCamera.Fov : 75.0f;
		return ModelPreviewFraming.ComputeCameraDistance(FramedBounds, verticalFovDegrees, GetPreviewViewportAspectRatio());
	}

	private float GetPreviewViewportAspectRatio()
	{
		if (PreviewSubViewport == null || !GodotObject.IsInstanceValid(PreviewSubViewport)) return 1.0f;

		Vector2I viewportSize = PreviewSubViewport.Size;
		return viewportSize.Y > 0 ? (float)viewportSize.X / viewportSize.Y : 1.0f;
	}

	private void OnPreviewViewportSizeChanged()
	{
		if (!ModelPreviewFraming.HasUsableBounds(FramedBounds)) return;

		DefaultDistance = ComputeFramedDistance();
		if (HasUserAdjustedCamera) return;

		CameraDistance = DefaultDistance;
		UpdateCameraTransform();
	}

	protected virtual void OnViewportGuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			HandleMouseButton(mb);
		}
		else if (@event is InputEventMouseMotion mm)
		{
			HandleMouseMotion(mm);
		}
	}

	private void HandleMouseButton(InputEventMouseButton mb)
	{
		if (mb.ButtonIndex == MouseButton.Left || mb.ButtonIndex == MouseButton.Right)
		{
			HandleLeftOrRightClick(mb);
		}
		else if (mb.ButtonIndex == MouseButton.Middle)
		{
			IsPanning = mb.Pressed;
			LastMousePosition = mb.Position;
		}
		else if (mb.ButtonIndex == MouseButton.WheelUp)
		{
			ZoomCamera(-1.0f);
		}
		else if (mb.ButtonIndex == MouseButton.WheelDown)
		{
			ZoomCamera(1.0f);
		}
	}

	private void HandleLeftOrRightClick(InputEventMouseButton mb)
	{
		if (mb.ButtonIndex == MouseButton.Right && (Input.IsKeyPressed(Key.Shift) || Input.IsKeyPressed(Key.Ctrl)))
		{
			IsPanning = mb.Pressed;
		}
		else
		{
			IsOrbiting = mb.Pressed;
		}

		LastMousePosition = mb.Position;
	}

	private void HandleMouseMotion(InputEventMouseMotion mm)
	{
		Vector2 delta = mm.Position - LastMousePosition;
		LastMousePosition = mm.Position;

		if (IsOrbiting)
		{
			HasUserAdjustedCamera = true;
			CameraYaw -= delta.X * 0.01f;
			CameraPitch -= delta.Y * 0.01f;
			UpdateCameraTransform();
		}
		else if (IsPanning && PreviewCamera != null && GodotObject.IsInstanceValid(PreviewCamera))
		{
			HasUserAdjustedCamera = true;
			Vector3 camRight = PreviewCamera.GlobalTransform.Basis.X;
			Vector3 camUp = PreviewCamera.GlobalTransform.Basis.Y;
			float panSpeed = CameraDistance * 0.0025f;
			TargetPosition -= (camRight * delta.X - camUp * delta.Y) * panSpeed;
			UpdateCameraTransform();
		}
	}

	private int _lightPresetIndex = 0;

	public virtual void TogglePreviewLight()
	{
		if (PreviewLight == null || !GodotObject.IsInstanceValid(PreviewLight)) return;

		_lightPresetIndex = (_lightPresetIndex + 1) % 4;
		switch (_lightPresetIndex)
		{
			case 0:
				PreviewLight.RotationDegrees = new Vector3(-30, 30, 0);
				break;
			case 1:
				PreviewLight.RotationDegrees = new Vector3(-45, -60, 0);
				break;
			case 2:
				PreviewLight.RotationDegrees = new Vector3(-60, 120, 0);
				break;
			case 3:
				PreviewLight.RotationDegrees = new Vector3(-15, -150, 0);
				break;
		}
	}
}