using System;
using Godot;
using Realm.Client.Core;

namespace Realm.Client.VFX;

[GlobalClass]
public partial class TerrainLine3D : Node3D
{
	private ImmediateMesh _immediateMesh;
	private MeshInstance3D _meshInstance;
	private StandardMaterial3D _material;

	public System.Numerics.Vector2 StartPos { get; set; }
	public System.Numerics.Vector2 EndPos { get; set; }
	public Color LineColor { get; set; } = Colors.White;
	public float Duration { get; set; } = 1.0f;
	public float RibbonWidth { get; set; } = 0.2f;

	private float _elapsedTime;
	private bool _isInitialized;

	public static TerrainLine3D Create(GameHost host, System.Numerics.Vector2 start, System.Numerics.Vector2 end, Color color, float duration = 1.0f, float width = 0.2f)
	{
		var line = new TerrainLine3D
		{
			Name = "TerrainLine3D",
			StartPos = start,
			EndPos = end,
			LineColor = color,
			Duration = duration,
			RibbonWidth = width
		};

		host.AddChild(line);
		line.Initialize(host);
		return line;
	}

	public void Initialize(GameHost host)
	{
		_elapsedTime = 0.0f;
		_immediateMesh = new ImmediateMesh();
		_meshInstance = new MeshInstance3D
		{
			Name = "TerrainLineMesh",
			Mesh = _immediateMesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled
		};

		_material = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = LineColor
		};

		_meshInstance.MaterialOverride = _material;
		AddChild(_meshInstance);

		BuildMesh(host);
		_isInitialized = true;
		SetProcess(true);
	}

	private void BuildMesh(GameHost host)
	{
		Vector2 p1 = new Vector2(StartPos.X, StartPos.Y);
		Vector2 p2 = new Vector2(EndPos.X, EndPos.Y);
		float dist = p1.DistanceTo(p2);
		if (dist < 0.001f) return;

		int segments = Math.Max(2, (int)Math.Ceiling(dist / 0.5f));
		Vector3[] points = new Vector3[segments + 1];

		for (int i = 0; i <= segments; i++)
		{
			float t = (float)i / segments;
			Vector2 groundPos = p1.Lerp(p2, t);
			float terrainY = host.GetTerrainHeightAt(new Vector3(groundPos.X, 0f, groundPos.Y));
			points[i] = new Vector3(groundPos.X, terrainY + 0.1f, groundPos.Y);
		}

		Vector2 dir2D = (p2 - p1).Normalized();
		if (dir2D.LengthSquared() < 0.0001f) dir2D = Vector2.Right;

		Vector3 side = new Vector3(-dir2D.Y, 0f, dir2D.X).Normalized();
		Vector3 widthOffset = side * (RibbonWidth * 0.5f);

		_immediateMesh.ClearSurfaces();
		_immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material);

		for (int i = 0; i < segments; i++)
		{
			Vector3 p0L = points[i] - widthOffset;
			Vector3 p0R = points[i] + widthOffset;
			Vector3 p1L = points[i + 1] - widthOffset;
			Vector3 p1R = points[i + 1] + widthOffset;

			float t0 = (float)i / segments;
			float t1 = (float)(i + 1) / segments;

			_immediateMesh.SurfaceSetColor(LineColor);
			_immediateMesh.SurfaceSetUV(new Vector2(t0, 0.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(p0L));

			_immediateMesh.SurfaceSetColor(LineColor);
			_immediateMesh.SurfaceSetUV(new Vector2(t0, 1.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(p0R));

			_immediateMesh.SurfaceSetColor(LineColor);
			_immediateMesh.SurfaceSetUV(new Vector2(t1, 0.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(p1L));

			_immediateMesh.SurfaceSetColor(LineColor);
			_immediateMesh.SurfaceSetUV(new Vector2(t0, 1.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(p0R));

			_immediateMesh.SurfaceSetColor(LineColor);
			_immediateMesh.SurfaceSetUV(new Vector2(t1, 1.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(p1R));

			_immediateMesh.SurfaceSetColor(LineColor);
			_immediateMesh.SurfaceSetUV(new Vector2(t1, 0.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(p1L));
		}

		_immediateMesh.SurfaceEnd();
	}

	public override void _Process(double delta)
	{
		if (!_isInitialized) return;

		float dt = (float)delta;
		_elapsedTime += dt;

		if (Duration > 0.0f)
		{
			if (_elapsedTime >= Duration)
			{
				QueueFree();
				return;
			}

			float alpha = Math.Clamp(1.0f - (_elapsedTime / Duration), 0.0f, 1.0f);
			_material.AlbedoColor = new Color(LineColor.R, LineColor.G, LineColor.B, LineColor.A * alpha);
		}
		else
		{
			QueueFree();
		}
	}
}
