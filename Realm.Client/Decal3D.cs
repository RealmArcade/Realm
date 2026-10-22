using System;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;

namespace Realm.Client;

[GlobalClass]
public partial class Decal3D : Decal
{
	public Entity Entity { get; set; }
	private string _decalId = "logo";

	private bool _normalEnabled = true;

	private Vector3 _baseSize;
	private Color _baseModulate = Colors.White;
	private float _baseEmissionEnergy = 0.0f;
	private float _shroudLightFactor = 1f;
	private double _animTime = 0.0;
	private bool _groundProjected;
	private float _groundOffset = 0.05f;
	private float _groundProjectedYawDegrees;
	private bool _hasAuthoredRotation;
	private Vector3 _authoredRotationDegrees;

	public DecalAnimationConfig Animation { get; set; } = new();

	public bool GroundProjected => _groundProjected;

	public float GroundOffset => _groundOffset;

	public bool NormalEnabled
	{
		get => _normalEnabled;
		set => _normalEnabled = value;
	}

	public string DecalId
	{
		get
		{
			if (GameContext.Host != null && GameContext.World.IsAlive(Entity)
			                              && GameContext.World.Has<DecalIdentity>(Entity))
				return GameContext.World.Get<DecalIdentity>(Entity).DecalId;
			return _decalId;
		}
		set
		{
			_decalId = value;
			if (GameContext.Host != null && GameContext.World.IsAlive(Entity))
			{
				var world = GameContext.World;
				world.SetOrAdd(Entity, new DecalIdentity(value));
			}
		}
	}

	public override void _Ready()
	{
		CullMask = RuntimeTerrain.TerrainDecalCullMask;
		_baseSize = Size;
		_baseModulate = Modulate;
		_baseEmissionEnergy = EmissionEnergy;

		GameContext.RaycastService?.RegisterDecal(this);

		UpdateProcessState();
	}

	public override void _ExitTree()
	{
		GameContext.RaycastService?.UnregisterDecal(this);
	}

	public void SetBaseProperties(Color modulate, float emissionEnergy, Vector3? baseSize = null)
	{
		_baseModulate = modulate;
		_baseEmissionEnergy = emissionEnergy;
		if (baseSize.HasValue && baseSize.Value.X > 0.001f)
		{
			_baseSize = baseSize.Value;
		}
		else if (_baseSize.X <= 0.001f)
		{
			_baseSize = Size;
		}

		UpdateProcessState();
	}

	public void SetShroudLightFactor(float factor)
	{
		factor = Mathf.Clamp(factor, 0f, 1f);
		if (Mathf.IsEqualApprox(_shroudLightFactor, factor)) return;
		_shroudLightFactor = factor;
		ApplyShroudModulate(_baseModulate);
	}

	private void ApplyShroudModulate(Color baseColor)
	{
		if (_shroudLightFactor >= 1f)
		{
			Modulate = baseColor;
			return;
		}

		Modulate = new Color(
			baseColor.R * _shroudLightFactor,
			baseColor.G * _shroudLightFactor,
			baseColor.B * _shroudLightFactor,
			baseColor.A);
	}

	public void SetPropertyAnimation(
		bool animateOpacity, float opacityPulseSpeed, float minOpacity, float maxOpacity,
		bool animateEmission, float emissionPulseSpeed, float minEmission, float maxEmission,
		bool animateScale, float scalePulseSpeed, float minScaleRatio, float maxScaleRatio,
		float upperFade = 0.3f, float lowerFade = 0.3f)
	{
		Animation.AnimateOpacity = animateOpacity;
		Animation.OpacityPulseSpeed = opacityPulseSpeed;
		Animation.MinOpacity = minOpacity;
		Animation.MaxOpacity = maxOpacity;

		Animation.AnimateEmission = animateEmission;
		Animation.EmissionPulseSpeed = emissionPulseSpeed;
		Animation.MinEmission = minEmission;
		Animation.MaxEmission = maxEmission;

		Animation.AnimateScale = animateScale;
		Animation.ScalePulseSpeed = scalePulseSpeed;
		Animation.MinScaleRatio = minScaleRatio;
		Animation.MaxScaleRatio = maxScaleRatio;

		UpperFade = upperFade;
		LowerFade = lowerFade;

		UpdateProcessState();
	}

	public void SetProjectionBox(float footprintSize, float projectionHeight)
	{
		if (footprintSize <= 0.001f) return;

		float height = projectionHeight > 0.001f ? projectionHeight : footprintSize;
		SetBaseProperties(_baseModulate, _baseEmissionEnergy, new Vector3(footprintSize, footprintSize, height));
		Size = _baseSize;
	}

	public void SetNormalFade(float normalFade)
	{
		NormalFade = Mathf.Clamp(normalFade, 0.0f, 1.0f);
	}

	public void SetDistanceFade(bool enabled, float begin, float end)
	{
		float clampedBegin = Mathf.Max(0.0f, begin);
		DistanceFadeEnabled = enabled;
		DistanceFadeBegin = clampedBegin;
		DistanceFadeLength = Mathf.Max(0.1f, end - clampedBegin);
	}

	public void SetGroundProjection(bool enabled, float groundOffset)
	{
		_groundOffset = Mathf.Max(0.0f, groundOffset);

		if (enabled == _groundProjected)
		{
			UpdateProcessState();
			return;
		}

		_groundProjected = enabled;
		if (enabled)
		{
			_authoredRotationDegrees = RotationDegrees;
			_groundProjectedYawDegrees = RotationDegrees.Y;
			_hasAuthoredRotation = true;
			UpdateGroundProjectedTransform();
		}
		else if (_hasAuthoredRotation)
		{
			RotationDegrees = _authoredRotationDegrees;
			_hasAuthoredRotation = false;
		}

		UpdateProcessState();
	}

	private void UpdateGroundProjectedTransform()
	{
		if (!_groundProjected || !IsInsideTree()) return;

		var editorService = GameContext.EditorService;
		if (editorService == null) return;

		Vector3 globalPosition = GlobalPosition;
		float groundHeight = editorService.GetTerrainHeightAt(globalPosition) + _groundOffset;
		float yawDegrees = GetParent() is Node3D parent ? parent.GlobalRotationDegrees.Y : _groundProjectedYawDegrees;

		Basis flatBasis = new Basis(Vector3.Up, Mathf.DegToRad(yawDegrees)) * new Basis(Vector3.Right, Mathf.DegToRad(-90.0f));
		GlobalTransform = new Transform3D(flatBasis, new Vector3(globalPosition.X, groundHeight, globalPosition.Z));
	}

	public void UpdateProcessState()
	{
		bool shouldProcess = Animation.AnimateOpacity || Animation.AnimateEmission || Animation.AnimateScale || _groundProjected;
		SetProcess(shouldProcess);
		if (!shouldProcess)
		{
			ApplyShroudModulate(_baseModulate);
			EmissionEnergy = _baseEmissionEnergy;
			if (_baseSize.X > 0.001f && _baseSize.Z > 0.001f)
			{
				Size = _baseSize;
			}
		}
	}

	public override void _Process(double delta)
	{
		if (_groundProjected)
		{
			UpdateGroundProjectedTransform();
		}

		if (!Animation.AnimateOpacity && !Animation.AnimateEmission && !Animation.AnimateScale)
		{
			SetProcess(_groundProjected);
			return;
		}

		_animTime += delta;
		float time = (float)_animTime;

		if (Animation.AnimateOpacity)
		{
			float sine = (MathF.Sin(time * Animation.OpacityPulseSpeed * MathF.PI * 2.0f) + 1.0f) * 0.5f;
			float alpha = Mathf.Lerp(Animation.MinOpacity, Animation.MaxOpacity, sine);
			var col = _baseModulate;
			col.A = Mathf.Clamp(alpha, 0.0f, 1.0f);
			ApplyShroudModulate(col);
		}
		else
		{
			ApplyShroudModulate(_baseModulate);
		}

		if (Animation.AnimateEmission)
		{
			float sine = (MathF.Sin(time * Animation.EmissionPulseSpeed * MathF.PI * 2.0f) + 1.0f) * 0.5f;
			EmissionEnergy = Mathf.Lerp(Animation.MinEmission, Animation.MaxEmission, sine);
		}
		else
		{
			EmissionEnergy = _baseEmissionEnergy;
		}

		if (Animation.AnimateScale && _baseSize.X > 0.001f)
		{
			float sine = (MathF.Sin(time * Animation.ScalePulseSpeed * MathF.PI * 2.0f) + 1.0f) * 0.5f;
			float ratio = Mathf.Lerp(Animation.MinScaleRatio, Animation.MaxScaleRatio, sine);
			Size = new Vector3(_baseSize.X * ratio, _baseSize.Y, _baseSize.Z * ratio);
		}
	}

	public void SetEditorCollisionEnabled(bool enabled)
	{
	}

	public void UpdateCollisionShape()
	{
	}
}