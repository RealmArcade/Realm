using Godot;
using System;

namespace Realm.Godot.VFX;

public enum ShockwaveType
{
	PlanarWave,
	ExpandingGroundRing,
	ExpandingBurstSphere
}

public partial class GroundShockwave3D : Node3D
{
	private ImmediateMesh _immediateMesh;
	private MeshInstance3D _meshInstance;
	private StandardMaterial3D _material;

	public ShockwaveType WaveType { get; set; } = ShockwaveType.ExpandingGroundRing;
	public Vector3 CenterPosition { get; set; }
	public Vector3 Direction { get; set; } = Vector3.Forward;
	public float StartRadius { get; set; } = 0.5f;
	public float MaxRadius { get; set; } = 8.0f;
	public float Speed { get; set; } = 12.0f;
	public float Duration { get; set; } = 1.0f;
	public float RingThickness { get; set; } = 0.8f;
	public Color WaveColor { get; set; } = new Color(1.0f, 0.5f, 0.1f, 0.9f);
	public bool ConformToTerrain { get; set; } = true;

	private float _elapsedTime;
	private float _currentRadius;
	private readonly Random _rng = new();

	public static GroundShockwave3D Create(Node parent, Vector3 center, ShockwaveType type, float maxRadius = 8.0f, float speed = 12.0f, float duration = 1.0f, Color? color = null)
	{
		var shockwave = new GroundShockwave3D();
		shockwave.Name = "GroundShockwave3D";
		shockwave.CenterPosition = center;
		shockwave.WaveType = type;
		shockwave.MaxRadius = maxRadius;
		shockwave.Speed = speed;
		shockwave.Duration = duration;
		if (color.HasValue) shockwave.WaveColor = color.Value;

		parent.AddChild(shockwave);
		shockwave.GlobalPosition = center;
		return shockwave;
	}

	public override void _Ready()
	{
		_immediateMesh = new ImmediateMesh();
		_meshInstance = new MeshInstance3D
		{
			Name = "ShockwaveMesh",
			Mesh = _immediateMesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled
		};

		_material = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BlendMode = BaseMaterial3D.BlendModeEnum.Add,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = WaveColor
		};

		_meshInstance.MaterialOverride = _material;
		AddChild(_meshInstance);
		_currentRadius = StartRadius;
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		_elapsedTime += dt;

		if (_elapsedTime >= Duration)
		{
			QueueFree();
			return;
		}

		_currentRadius = Math.Min(MaxRadius, StartRadius + (Speed * _elapsedTime));
		float progress = Math.Clamp(_elapsedTime / Duration, 0.0f, 1.0f);
		float alpha = Math.Clamp(1.0f - progress, 0.0f, 1.0f);

		_material.AlbedoColor = new Color(WaveColor.R, WaveColor.G, WaveColor.B, WaveColor.A * alpha);

		switch (WaveType)
		{
			case ShockwaveType.PlanarWave:
				RedrawPlanarWave(alpha);
				break;
			case ShockwaveType.ExpandingGroundRing:
				RedrawGroundRing(alpha);
				break;
			case ShockwaveType.ExpandingBurstSphere:
				RedrawBurstSphere(alpha);
				break;
		}
	}

	private void RedrawGroundRing(float alpha)
	{
		if (_immediateMesh == null) return;
		_immediateMesh.ClearSurfaces();

		int segments = 36;
		float innerR = Math.Max(0.1f, _currentRadius - RingThickness * 0.5f);
		float outerR = _currentRadius + RingThickness * 0.5f;

		_immediateMesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _material);

		for (int i = 0; i <= segments; i++)
		{
			float angle = (float)i / segments * Mathf.Tau;
			float cos = Mathf.Cos(angle);
			float sin = Mathf.Sin(angle);

			Vector3 innerPos = CenterPosition + new Vector3(cos * innerR, 0.05f, sin * innerR);
			Vector3 outerPos = CenterPosition + new Vector3(cos * outerR, 0.05f, sin * outerR);

			if (ConformToTerrain && RuntimeTerrain.Instance != null)
			{
				RuntimeTerrain.Instance.GetHeightAndNormal(innerPos.X, innerPos.Z, out float inH, out Vector3 inN);
				RuntimeTerrain.Instance.GetHeightAndNormal(outerPos.X, outerPos.Z, out float outH, out Vector3 outN);
				innerPos.Y = inH + 0.08f;
				outerPos.Y = outH + 0.08f;
			}

			Color col = new Color(WaveColor.R, WaveColor.G, WaveColor.B, WaveColor.A * alpha);

			_immediateMesh.SurfaceSetColor(col);
			_immediateMesh.SurfaceSetUV(new Vector2((float)i / segments, 0.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(innerPos));

			_immediateMesh.SurfaceSetColor(col);
			_immediateMesh.SurfaceSetUV(new Vector2((float)i / segments, 1.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(outerPos));
		}

		_immediateMesh.SurfaceEnd();
	}

	private void RedrawPlanarWave(float alpha)
	{
		if (_immediateMesh == null) return;
		_immediateMesh.ClearSurfaces();

		Vector3 fwd = Direction.LengthSquared() > 0.001f ? Direction.Normalized() : Vector3.Forward;
		Vector3 right = fwd.Cross(Vector3.Up).Normalized();
		if (right.LengthSquared() < 0.001f) right = Vector3.Right;

		float halfWidth = MaxRadius * 0.6f;
		float waveFrontDist = Speed * _elapsedTime;
		int widthSegments = 16;

		_immediateMesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, _material);

		for (int i = 0; i <= widthSegments; i++)
		{
			float t = (float)i / widthSegments;
			float offsetW = (t - 0.5f) * 2.0f * halfWidth;

			Vector3 backPos = CenterPosition + (right * offsetW) + (fwd * Math.Max(0.0f, waveFrontDist - RingThickness));
			Vector3 frontPos = CenterPosition + (right * offsetW) + (fwd * waveFrontDist);

			if (ConformToTerrain && RuntimeTerrain.Instance != null)
			{
				RuntimeTerrain.Instance.GetHeightAndNormal(backPos.X, backPos.Z, out float bH, out Vector3 _);
				RuntimeTerrain.Instance.GetHeightAndNormal(frontPos.X, frontPos.Z, out float fH, out Vector3 _);
				backPos.Y = bH + 0.1f;
				frontPos.Y = fH + 0.2f;
			}

			Color col = new Color(WaveColor.R, WaveColor.G, WaveColor.B, WaveColor.A * alpha);

			_immediateMesh.SurfaceSetColor(col);
			_immediateMesh.SurfaceSetUV(new Vector2(t, 0.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(backPos));

			_immediateMesh.SurfaceSetColor(col);
			_immediateMesh.SurfaceSetUV(new Vector2(t, 1.0f));
			_immediateMesh.SurfaceAddVertex(ToLocal(frontPos));
		}

		_immediateMesh.SurfaceEnd();
	}

	private void RedrawBurstSphere(float alpha)
	{
		if (_immediateMesh == null) return;
		_immediateMesh.ClearSurfaces();

		int latitudeBands = 12;
		int longitudeBands = 18;
		float r = _currentRadius;

		_immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material);

		for (int lat = 0; lat < latitudeBands; lat++)
		{
			float theta1 = (float)lat / latitudeBands * Mathf.Pi;
			float theta2 = (float)(lat + 1) / latitudeBands * Mathf.Pi;

			float sinT1 = Mathf.Sin(theta1);
			float cosT1 = Mathf.Cos(theta1);
			float sinT2 = Mathf.Sin(theta2);
			float cosT2 = Mathf.Cos(theta2);

			for (int lon = 0; lon < longitudeBands; lon++)
			{
				float phi1 = (float)lon / longitudeBands * Mathf.Tau;
				float phi2 = (float)(lon + 1) / longitudeBands * Mathf.Tau;

				Vector3 v1 = new Vector3(r * sinT1 * Mathf.Cos(phi1), r * cosT1, r * sinT1 * Mathf.Sin(phi1));
				Vector3 v2 = new Vector3(r * sinT2 * Mathf.Cos(phi1), r * cosT2, r * sinT2 * Mathf.Sin(phi1));
				Vector3 v3 = new Vector3(r * sinT2 * Mathf.Cos(phi2), r * cosT2, r * sinT2 * Mathf.Sin(phi2));
				Vector3 v4 = new Vector3(r * sinT1 * Mathf.Cos(phi2), r * cosT1, r * sinT1 * Mathf.Sin(phi2));

				Color col = new Color(WaveColor.R, WaveColor.G, WaveColor.B, WaveColor.A * alpha);

				_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceAddVertex(v1);
				_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceAddVertex(v2);
				_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceAddVertex(v3);

				_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceAddVertex(v1);
				_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceAddVertex(v3);
				_immediateMesh.SurfaceSetColor(col); _immediateMesh.SurfaceAddVertex(v4);
			}
		}

		_immediateMesh.SurfaceEnd();
	}
}
