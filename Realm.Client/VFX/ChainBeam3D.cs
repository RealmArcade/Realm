using Godot;
using System;
using System.Collections.Generic;

namespace Realm.Client.VFX;

public partial class ChainBeam3D : Node3D
{
	private readonly List<Vector3> _targetPositions = new();
	private ImmediateMesh _immediateMesh;
	private MeshInstance3D _meshInstance;
	private StandardMaterial3D _material;

	public List<Vector3> TargetPositions => _targetPositions;
	public float JumpDelay { get; set; } = 0.05f;
	public int ForkCount { get; set; } = 0;
	public float FadeLifetime { get; set; } = 0.4f;
	public float RibbonWidth { get; set; } = 0.35f;
	public Color BeamColor { get; set; } = new Color(0.3f, 0.7f, 1.0f, 1.0f);
	public float JitterAmount { get; set; } = 0.25f;
	public bool IsAdditive { get; set; } = true;
	public Texture2D? BeamTexture { get; set; }

	private float _elapsedTime;
	private float _activeJumpIndex;
	private bool _isInitialized;
	private readonly Random _rng = new();

	public static ChainBeam3D Create(Node parent, IEnumerable<Vector3> positions, float jumpDelay = 0.05f, int forkCount = 0, float fadeLifetime = 0.4f, float width = 0.35f, Color? color = null)
	{
		var beam = new ChainBeam3D();
		beam.Name = "ChainBeam3D";
		beam._targetPositions.AddRange(positions);
		beam.JumpDelay = jumpDelay;
		beam.ForkCount = forkCount;
		beam.FadeLifetime = fadeLifetime;
		beam.RibbonWidth = width;
		if (color.HasValue) beam.BeamColor = color.Value;

		parent.AddChild(beam);
		beam.Initialize();
		return beam;
	}

	public override void _Ready()
	{
		_immediateMesh = new ImmediateMesh();
		_meshInstance = new MeshInstance3D
		{
			Name = "ChainBeamMesh",
			Mesh = _immediateMesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled
		};

		_material = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = IsAdditive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = BeamColor
		};

		if (BeamTexture != null)
		{
			_material.AlbedoTexture = BeamTexture;
		}

		_meshInstance.MaterialOverride = _material;
		AddChild(_meshInstance);
	}

	public void Initialize()
	{
		_elapsedTime = 0.0f;
		_activeJumpIndex = 0.0f;
		_isInitialized = true;
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (!_isInitialized || _targetPositions.Count < 2)
		{
			QueueFree();
			return;
		}

		float dt = (float)delta;
		_elapsedTime += dt;

		int totalJumps = _targetPositions.Count - 1;
		float totalJumpTime = totalJumps * Math.Max(0.01f, JumpDelay);
		float totalDuration = totalJumpTime + FadeLifetime;

		if (_elapsedTime >= totalDuration)
		{
			QueueFree();
			return;
		}

		int activeCount = JumpDelay > 0.001f
			? Math.Min(_targetPositions.Count, 1 + (int)(_elapsedTime / JumpDelay))
			: _targetPositions.Count;

		float fadeProgress = _elapsedTime > totalJumpTime
			? Math.Clamp((_elapsedTime - totalJumpTime) / Math.Max(0.01f, FadeLifetime), 0.0f, 1.0f)
			: 0.0f;

		float currentAlpha = Math.Clamp(1.0f - fadeProgress, 0.0f, 1.0f);
		_material.AlbedoColor = new Color(BeamColor.R, BeamColor.G, BeamColor.B, BeamColor.A * currentAlpha);

		RedrawBeam(activeCount, currentAlpha);
	}

	private Vector3 GetCameraPosition()
	{
		Camera3D camera = GetViewport()?.GetCamera3D();
		return camera != null && GodotObject.IsInstanceValid(camera)
			? camera.GlobalPosition
			: GlobalPosition + new Vector3(0, 5, 10);
	}

	private void DrawForks(Vector3 p1, Vector3 p2, Vector3 camPos, float alpha)
	{
		for (int f = 0; f < ForkCount; f++)
		{
			Vector3 mid = p1.Lerp(p2, 0.5f);
			Vector3 forkDir = (p2 - p1).Cross(Vector3.Up).Normalized();
			if (f % 2 == 1) forkDir = -forkDir;
			
			Vector3 jitter = new Vector3(0, (float)(_rng.NextDouble() - 0.5) * JitterAmount, 0);
			Vector3 forkEnd = mid + (forkDir * (1.5f + JitterAmount * 2.0f)) + jitter;
			
			DrawSegment(mid, forkEnd, camPos, alpha * 0.6f);
		}
	}

	private void RedrawBeam(int activePointCount, float alpha)
	{
		if (_immediateMesh == null) return;
		_immediateMesh.ClearSurfaces();
		if (activePointCount < 2) return;

		Vector3 camPos = GetCameraPosition();

		_immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material);

		for (int i = 0; i < activePointCount - 1; i++)
		{
			Vector3 p1 = _targetPositions[i];
			Vector3 p2 = _targetPositions[i + 1];

			DrawSegment(p1, p2, camPos, alpha);

			if (ForkCount <= 0 || i >= _targetPositions.Count - 1) continue;

			DrawForks(p1, p2, camPos, alpha);
		}

		_immediateMesh.SurfaceEnd();
	}

	private void DrawSegment(Vector3 p1, Vector3 p2, Vector3 camPos, float alpha)
	{
		Vector3 dir = p2 - p1;
		float dist = dir.Length();
		if (dist < 0.001f) return;

		int subSegments = Math.Max(2, (int)(dist * 2.0f));
		Vector3 forward = dir / dist;

		Vector3 side = forward.Cross((p1 - camPos).Normalized()).Normalized();
		if (side.LengthSquared() < 0.001f) side = Vector3.Right;

		float halfWidth = RibbonWidth * 0.5f;
		Vector3 widthOffset = side * halfWidth;
		Color col = new Color(BeamColor.R, BeamColor.G, BeamColor.B, BeamColor.A * alpha);

		for (int s = 0; s < subSegments; s++)
		{
			float t0 = (float)s / subSegments;
			float t1 = (float)(s + 1) / subSegments;

			Vector3 basePos0 = p1.Lerp(p2, t0);
			Vector3 basePos1 = p1.Lerp(p2, t1);

			Vector3 jitter0 = (s > 0) ? new Vector3((float)(_rng.NextDouble() - 0.5) * JitterAmount, (float)(_rng.NextDouble() - 0.5) * JitterAmount, (float)(_rng.NextDouble() - 0.5) * JitterAmount) : Vector3.Zero;
			Vector3 jitter1 = (s + 1 < subSegments) ? new Vector3((float)(_rng.NextDouble() - 0.5) * JitterAmount, (float)(_rng.NextDouble() - 0.5) * JitterAmount, (float)(_rng.NextDouble() - 0.5) * JitterAmount) : Vector3.Zero;

			Vector3 currPos0 = basePos0 + jitter0;
			Vector3 currPos1 = basePos1 + jitter1;

			Vector3 v0 = currPos0 - widthOffset;
			Vector3 v1 = currPos0 + widthOffset;
			Vector3 v2 = currPos1 - widthOffset;
			Vector3 v3 = currPos1 + widthOffset;

			_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceSetUV(new Vector2(t0, 0.0f)); _immediateMesh.SurfaceAddVertex(ToLocal(v0));
			_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceSetUV(new Vector2(t0, 1.0f)); _immediateMesh.SurfaceAddVertex(ToLocal(v1));
			_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceSetUV(new Vector2(t1, 0.0f)); _immediateMesh.SurfaceAddVertex(ToLocal(v2));

			_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceSetUV(new Vector2(t0, 1.0f)); _immediateMesh.SurfaceAddVertex(ToLocal(v1));
			_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceSetUV(new Vector2(t1, 1.0f)); _immediateMesh.SurfaceAddVertex(ToLocal(v3));
			_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceSetUV(new Vector2(t1, 0.0f)); _immediateMesh.SurfaceAddVertex(ToLocal(v2));
		}
	}
}
