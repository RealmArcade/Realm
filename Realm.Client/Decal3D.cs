using System;
using Arch.Core;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;

namespace Realm.Client;

public partial class Decal3D : Decal
{
	public Entity Entity { get; set; }
	private string _decalId = "logo";

	private StaticBody3D _staticBody;
	private CollisionShape3D _collisionShape;
	private bool _normalEnabled = true;

	private Vector3 _baseSize;
	private Color _baseModulate = Colors.White;
	private float _baseEmissionEnergy = 0.0f;
	private double _animTime = 0.0;

	public Realm.Shared.Metadata.DecalAnimationSettings AnimationSettings { get; set; } = new Realm.Shared.Metadata.DecalAnimationSettings();

	public bool AnimateOpacity { get => AnimationSettings.AnimateOpacity; set => AnimationSettings.AnimateOpacity = value; }
	public float OpacityPulseSpeed { get => AnimationSettings.OpacityPulseSpeed; set => AnimationSettings.OpacityPulseSpeed = value; }
	public float MinOpacity { get => AnimationSettings.MinOpacity; set => AnimationSettings.MinOpacity = value; }
	public float MaxOpacity { get => AnimationSettings.MaxOpacity; set => AnimationSettings.MaxOpacity = value; }

	public bool AnimateEmission { get => AnimationSettings.AnimateEmission; set => AnimationSettings.AnimateEmission = value; }
	public float EmissionPulseSpeed { get => AnimationSettings.EmissionPulseSpeed; set => AnimationSettings.EmissionPulseSpeed = value; }
	public float MinEmission { get => AnimationSettings.MinEmission; set => AnimationSettings.MinEmission = value; }
	public float MaxEmission { get => AnimationSettings.MaxEmission; set => AnimationSettings.MaxEmission = value; }

	public bool AnimateScale { get => AnimationSettings.AnimateScale; set => AnimationSettings.AnimateScale = value; }
	public float ScalePulseSpeed { get => AnimationSettings.ScalePulseSpeed; set => AnimationSettings.ScalePulseSpeed = value; }
	public float MinScaleRatio { get => AnimationSettings.MinScaleRatio; set => AnimationSettings.MinScaleRatio = value; }
	public float MaxScaleRatio { get => AnimationSettings.MaxScaleRatio; set => AnimationSettings.MaxScaleRatio = value; }

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

		bool isEditor = GameContext.EditorService.IsMapEditorMode == true;
		_staticBody = new StaticBody3D();
		_staticBody.CollisionLayer = isEditor ? 1u : 0u;
		_staticBody.CollisionMask = 0;
		AddChild(_staticBody);

		_collisionShape = new CollisionShape3D();
		var box = new BoxShape3D();
		box.Size = new Vector3(Size.X, 0.5f, Size.Z);
		_collisionShape.Shape = box;
		_staticBody.AddChild(_collisionShape);

		UpdateProcessState();
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

	public void SetPropertyAnimation(
		bool animateOpacity, float opacityPulseSpeed, float minOpacity, float maxOpacity,
		bool animateEmission, float emissionPulseSpeed, float minEmission, float maxEmission,
		bool animateScale, float scalePulseSpeed, float minScaleRatio, float maxScaleRatio,
		float upperFade = 0.3f, float lowerFade = 0.3f)
	{
		AnimateOpacity = animateOpacity;
		OpacityPulseSpeed = opacityPulseSpeed;
		MinOpacity = minOpacity;
		MaxOpacity = maxOpacity;

		AnimateEmission = animateEmission;
		EmissionPulseSpeed = emissionPulseSpeed;
		MinEmission = minEmission;
		MaxEmission = maxEmission;

		AnimateScale = animateScale;
		ScalePulseSpeed = scalePulseSpeed;
		MinScaleRatio = minScaleRatio;
		MaxScaleRatio = maxScaleRatio;

		UpperFade = upperFade;
		LowerFade = lowerFade;

		UpdateProcessState();
	}

	public void UpdateProcessState()
	{
		bool shouldProcess = AnimateOpacity || AnimateEmission || AnimateScale;
		SetProcess(shouldProcess);
		if (!shouldProcess)
		{
			Modulate = _baseModulate;
			EmissionEnergy = _baseEmissionEnergy;
			if (_baseSize.X > 0.001f && _baseSize.Z > 0.001f)
			{
				Size = _baseSize;
				UpdateCollisionShape();
			}
		}
	}

	public override void _Process(double delta)
	{
		if (!AnimateOpacity && !AnimateEmission && !AnimateScale)
		{
			SetProcess(false);
			return;
		}

		_animTime += delta;
		float time = (float)_animTime;

		if (AnimateOpacity)
		{
			float sine = (MathF.Sin(time * OpacityPulseSpeed * MathF.PI * 2.0f) + 1.0f) * 0.5f;
			float alpha = Mathf.Lerp(MinOpacity, MaxOpacity, sine);
			var col = _baseModulate;
			col.A = Mathf.Clamp(alpha, 0.0f, 1.0f);
			Modulate = col;
		}
		else
		{
			Modulate = _baseModulate;
		}

		if (AnimateEmission)
		{
			float sine = (MathF.Sin(time * EmissionPulseSpeed * MathF.PI * 2.0f) + 1.0f) * 0.5f;
			EmissionEnergy = Mathf.Lerp(MinEmission, MaxEmission, sine);
		}
		else
		{
			EmissionEnergy = _baseEmissionEnergy;
		}

		if (AnimateScale && _baseSize.X > 0.001f)
		{
			float sine = (MathF.Sin(time * ScalePulseSpeed * MathF.PI * 2.0f) + 1.0f) * 0.5f;
			float ratio = Mathf.Lerp(MinScaleRatio, MaxScaleRatio, sine);
			Size = new Vector3(_baseSize.X * ratio, _baseSize.Y, _baseSize.Z * ratio);
			UpdateCollisionShape();
		}
	}

	public void SetEditorCollisionEnabled(bool enabled)
	{
		if (_staticBody != null && GodotObject.IsInstanceValid(_staticBody))
		{
			_staticBody.CollisionLayer = enabled ? 1u : 0u;
		}
	}

	public void UpdateCollisionShape()
	{
		if (_collisionShape != null && _collisionShape.Shape is BoxShape3D box)
		{
			box.Size = new Vector3(Size.X, 0.5f, Size.Z);
		}
	}
}