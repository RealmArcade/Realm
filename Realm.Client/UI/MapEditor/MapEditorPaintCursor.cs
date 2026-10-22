using Godot;
using Realm.Client.Core;

namespace Realm.Client.UI.MapEditor;

[GlobalClass]
public partial class MapEditorPaintCursor : Node3D
{
	private static readonly Color GroundColor = new(0.15f, 0.65f, 1.0f, 0.3f);
	private static readonly Color CliffColor = new(1.0f, 0.55f, 0.12f, 0.45f);
	private const float SurfaceOffset = 0.08f;

	private MeshInstance3D? _surfaceMesh;
	private MeshInstance3D? _facingMesh;
	private StandardMaterial3D? _surfaceMaterial;
	private StandardMaterial3D? _facingMaterial;
	private bool _brushIsSquare;

	public EditorPaintSurface CurrentSurface { get; private set; } = EditorPaintSurface.None;

	public void SetBrushShape(bool isSquare)
	{
		_brushIsSquare = isSquare;
		if (_surfaceMesh == null) return;

		_surfaceMesh.Mesh = isSquare ? CreateSquareMesh() : CreateCircularMesh();
	}

	public void ShowAt(Vector3 surfacePosition, Vector3 surfaceNormal, float radius, EditorPaintSurface surface)
	{
		EnsureVisuals();
		if (_surfaceMesh == null || surface == EditorPaintSurface.None || radius <= 0.0f)
		{
			HideCursor();
			return;
		}

		Vector3 normal = surfaceNormal.LengthSquared() > 0.0001f ? surfaceNormal.Normalized() : Vector3.Up;
		Vector3 facingDirection = new(normal.X, 0.0f, normal.Z);
		if (surface == EditorPaintSurface.Cliff && facingDirection.LengthSquared() <= 0.0001f)
		{
			surface = EditorPaintSurface.Ground;
		}

		CurrentSurface = surface;
		ApplySurfaceAppearance(surface);

		Basis alignedBasis = GameHost.CreateAlignedBasis(normal).Scaled(Vector3.One * radius);
		Transform = new Transform3D(alignedBasis, surfacePosition + normal * SurfaceOffset);
		Visible = true;

		if (_facingMesh != null)
		{
			_facingMesh.Visible = surface == EditorPaintSurface.Cliff;
			if (_facingMesh.Visible)
			{
				OrientFacingIndicator(normal, facingDirection);
			}
		}
	}

	public void HideCursor()
	{
		Visible = false;
		CurrentSurface = EditorPaintSurface.None;
	}

	private void EnsureVisuals()
	{
		if (_surfaceMesh != null) return;

		_surfaceMaterial = CreateOverlayMaterial(GroundColor);
		_surfaceMesh = new MeshInstance3D
		{
			Name = "PaintCursorSurface",
			Mesh = _brushIsSquare ? CreateSquareMesh() : CreateCircularMesh(),
			MaterialOverride = _surfaceMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled
		};
		AddChild(_surfaceMesh);

		_facingMaterial = CreateOverlayMaterial(new Color(CliffColor.R, CliffColor.G, CliffColor.B, 0.95f));
		_facingMesh = new MeshInstance3D
		{
			Name = "PaintCursorCliffFacing",
			Mesh = CreateFacingArrowMesh(),
			MaterialOverride = _facingMaterial,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled
		};
		AddChild(_facingMesh);
	}

	private static StandardMaterial3D CreateOverlayMaterial(Color color)
	{
		var material = new StandardMaterial3D
		{
			AlbedoColor = color,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled
		};
		material.EmissionEnabled = true;
		material.Emission = new Color(color.R, color.G, color.B) * 0.5f;
		material.DisableReceiveShadows = true;
		return material;
	}

	private void ApplySurfaceAppearance(EditorPaintSurface surface)
	{
		if (_surfaceMaterial == null) return;

		Color color = surface == EditorPaintSurface.Cliff ? CliffColor : GroundColor;
		_surfaceMaterial.AlbedoColor = color;
		_surfaceMaterial.Emission = new Color(color.R, color.G, color.B) * 0.5f;
	}

	private void OrientFacingIndicator(Vector3 normal, Vector3 facingDirection)
	{
		if (_facingMesh == null) return;

		Vector3 projectedFacing = facingDirection - normal * facingDirection.Dot(normal);
		if (projectedFacing.LengthSquared() <= 0.0001f)
		{
			_facingMesh.Visible = false;
			return;
		}

		projectedFacing = projectedFacing.Normalized();
		Vector3 right = normal.Cross(projectedFacing).Normalized();
		_facingMesh.Transform = new Transform3D(new Basis(right, normal, projectedFacing), Vector3.Zero);
	}

	private static PlaneMesh CreateSquareMesh()
	{
		return new PlaneMesh { Size = new Vector2(EditableTerrain.DefaultQuadSize, EditableTerrain.DefaultQuadSize) };
	}

	private static TorusMesh CreateCircularMesh()
	{
		return new TorusMesh { InnerRadius = 0.95f, OuterRadius = 1.05f };
	}

	private static ArrayMesh CreateFacingArrowMesh()
	{
		var vertices = new Vector3[]
		{
			new(-0.22f, 0.0f, 0.6f),
			new(0.22f, 0.0f, 0.6f),
			new(0.0f, 0.0f, 1.1f)
		};

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1 };

		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}
}
