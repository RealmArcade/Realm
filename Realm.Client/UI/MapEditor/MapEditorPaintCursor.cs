using Godot;
using Realm.Client.Core;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class MapEditorPaintCursor : Node3D
{
	private static readonly Color NeutralColor = new(0.15f, 0.65f, 1.0f, 0.3f);
	private static readonly Color GroundOnlyColor = new(1.0f, 0.15f, 0.85f, 0.4f);
	private static readonly Color GroundAndCliffColor = new(0.1f, 0.9f, 1.0f, 0.4f);
	private static readonly Color CliffOnlyColor = new(1.0f, 0.72f, 0.1f, 0.45f);
	private const float GroundSurfaceOffset = 0.02f;
	private const float CliffSurfaceOffset = 0.1f;
	private const float SquareBrushAlpha = 0.2f;
	private const int OverlayRenderPriority = 10;

	private MeshInstance3D? _surfaceMesh;
	private StandardMaterial3D? _surfaceMaterial;
	private bool _brushIsSquare;

	public EditorPaintSurfaceMode? CurrentPaintMode { get; private set; }

	public void SetBrushShape(bool isSquare)
	{
		_brushIsSquare = isSquare;
		if (_surfaceMesh == null) return;

		_surfaceMesh.Mesh = isSquare ? CreateSquareMesh() : CreateCircularMesh();
	}

	public void ShowAt(Vector3 position, float radius, EditorPaintSurfaceMode? paintMode)
	{
		EnsureVisuals();
		if (_surfaceMesh == null || radius <= 0.0f)
		{
			HideCursor();
			return;
		}

		CurrentPaintMode = paintMode;
		ApplyColor(ResolveColor(paintMode));

		bool isVertical = paintMode == EditorPaintSurfaceMode.CliffOnly;
		Basis orientation = isVertical ? CreateVerticalBasis(position) : Basis.Identity;
		Vector3 offsetDirection = isVertical ? orientation.Y : Vector3.Up;
		float surfaceOffset = isVertical ? CliffSurfaceOffset : GroundSurfaceOffset;

		Transform = new Transform3D(orientation.Scaled(Vector3.One * radius), position + offsetDirection * surfaceOffset);
		Visible = true;
	}

	public void HideCursor()
	{
		Visible = false;
		CurrentPaintMode = null;
	}

	private static Color ResolveColor(EditorPaintSurfaceMode? paintMode)
	{
		return paintMode switch
		{
			EditorPaintSurfaceMode.GroundOnly => GroundOnlyColor,
			EditorPaintSurfaceMode.GroundAndCliff => GroundAndCliffColor,
			EditorPaintSurfaceMode.CliffOnly => CliffOnlyColor,
			_ => NeutralColor
		};
	}

	private Basis CreateVerticalBasis(Vector3 position)
	{
		Vector3 facing = ResolveViewerFacing(position);
		Vector3 right = Vector3.Up.Cross(facing);
		right = right.LengthSquared() > 0.0001f ? right.Normalized() : Vector3.Right;
		return new Basis(right, facing, right.Cross(facing));
	}

	private Vector3 ResolveViewerFacing(Vector3 position)
	{
		var camera = GetViewport()?.GetCamera3D();
		if (camera == null) return Vector3.Back;

		Vector3 toCamera = camera.GlobalPosition - position;
		toCamera.Y = 0.0f;
		return toCamera.LengthSquared() > 0.0001f ? toCamera.Normalized() : Vector3.Back;
	}

	private void EnsureVisuals()
	{
		if (_surfaceMesh != null) return;

		_surfaceMaterial = CreateOverlayMaterial(NeutralColor);
		_surfaceMesh = new MeshInstance3D
		{
			Name = "PaintCursorSurface",
			Mesh = _brushIsSquare ? CreateSquareMesh() : CreateCircularMesh(),
			MaterialOverride = _surfaceMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled
		};
		AddChild(_surfaceMesh);
	}

	private static StandardMaterial3D CreateOverlayMaterial(Color color)
	{
		var material = new StandardMaterial3D
		{
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			NoDepthTest = true,
			RenderPriority = OverlayRenderPriority
		};
		material.EmissionEnabled = true;
		material.Emission = new Color(color.R, color.G, color.B) * 0.5f;
		material.DisableReceiveShadows = true;
		return material;
	}

	private void ApplyColor(Color color)
	{
		if (_surfaceMaterial == null) return;

		if (_brushIsSquare) color.A = SquareBrushAlpha;

		_surfaceMaterial.AlbedoColor = color;
		_surfaceMaterial.Emission = new Color(color.R, color.G, color.B) * 0.5f;
	}

	private static PlaneMesh CreateSquareMesh()
	{
		return new PlaneMesh { Size = new Vector2(EditableTerrain.DefaultQuadSize, EditableTerrain.DefaultQuadSize) };
	}

	private static TorusMesh CreateCircularMesh()
	{
		return new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1.05f };
	}
}
