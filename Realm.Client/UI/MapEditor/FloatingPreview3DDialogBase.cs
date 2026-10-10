using Godot;
using System;

namespace Realm.Client.UI.MapEditor;

public partial class FloatingPreview3DDialogBase : FloatingDialogBase
{
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
		CameraPresetToolbar.AddThemeConstantOverride("separation", 4);

		var lblPreset = new Label();
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
			var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
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
		UpdateCameraTransform();
	}

	public virtual void ZoomCamera(float direction)
	{
		float factor = direction > 0 ? 1.15f : 0.85f;
		CameraDistance = Mathf.Clamp(CameraDistance * factor, Math.Max(0.5f, DefaultDistance * 0.15f), DefaultDistance * 6.0f);
		UpdateCameraTransform();
	}

	public virtual void UpdateCameraTransform()
	{
		if (PreviewCamera == null || !GodotObject.IsInstanceValid(PreviewCamera)) return;

		CameraPitch = Mathf.Clamp(CameraPitch, -1.45f, 1.45f);
		float cosPitch = Mathf.Cos(CameraPitch);
		float sinPitch = Mathf.Sin(CameraPitch);
		float cosYaw = Mathf.Cos(CameraYaw);
		float sinYaw = Mathf.Sin(CameraYaw);

		Vector3 offset = new Vector3(
			CameraDistance * cosPitch * sinYaw,
			CameraDistance * sinPitch,
			CameraDistance * cosPitch * cosYaw
		);

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

		Aabb totalAabb = new Aabb();
		bool hasMesh = false;

		CollectAabb(rootNode, Transform3D.Identity, ref totalAabb, ref hasMesh);

		if (hasMesh && totalAabb.Size.LengthSquared() > 0.001f)
		{
			TargetPosition = totalAabb.GetCenter();
			float radius = totalAabb.Size.Length() * 0.6f;
			DefaultDistance = Mathf.Clamp(radius * 2.2f, 1.5f, 50.0f);
		}
		else
		{
			TargetPosition = new Vector3(0, 0.8f, 0);
			DefaultDistance = 3.0f;
		}

		CameraDistance = DefaultDistance;
		UpdateCameraTransform();
	}

	private void CollectAabb(Node node, Transform3D parentTransform, ref Aabb totalAabb, ref bool hasMesh)
	{
		Transform3D currentTransform = parentTransform;
		if (node is Node3D n3D)
		{
			currentTransform = parentTransform * n3D.Transform;
		}

		ProcessMeshInstance(node, currentTransform, ref totalAabb, ref hasMesh);

		int childCount = node.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			CollectAabb(node.GetChild(i), currentTransform, ref totalAabb, ref hasMesh);
		}
	}

	private void ProcessMeshInstance(Node node, Transform3D currentTransform, ref Aabb totalAabb, ref bool hasMesh)
	{
		if (node is not MeshInstance3D meshInstance || meshInstance.Mesh == null) return;

		Aabb globalMeshAabb = GetGlobalAabb(meshInstance, currentTransform);

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

	private Aabb GetGlobalAabb(MeshInstance3D meshInstance, Transform3D currentTransform)
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

		return globalMeshAabb;
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
			CameraYaw -= delta.X * 0.01f;
			CameraPitch -= delta.Y * 0.01f;
			UpdateCameraTransform();
		}
		else if (IsPanning && PreviewCamera != null && GodotObject.IsInstanceValid(PreviewCamera))
		{
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