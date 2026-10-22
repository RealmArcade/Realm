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

	public DecalAnimationConfig Animation { get; set; } = new();

	public bool NormalEnabled
	{
		get => _normalEnabled;
		set => _normalEnabled = value;
	}

	public string DecalId
	{
		get
		{
			if (GameHost.Instance != null && GameContext.World.IsAlive(Entity)
			                              && GameContext.World.Has<DecalIdentity>(Entity))
				return GameContext.World.Get<DecalIdentity>(Entity).DecalId;
			return _decalId;
		}
		set
		{
			_decalId = value;
			if (GameHost.Instance != null && GameContext.World.IsAlive(Entity))
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

	public void UpdateProcessState()
	{
		bool shouldProcess = Animation.AnimateOpacity || Animation.AnimateEmission || Animation.AnimateScale;
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
		if (!Animation.AnimateOpacity && !Animation.AnimateEmission && !Animation.AnimateScale)
		{
			SetProcess(false);
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