using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;

namespace Realm.Ecs.AI;

public class BotController
{
	private readonly AffordanceScanner _scanner = new();
	private readonly LinearUtilityPolicy _policy = new();
	private readonly List<GenericAffordance> _affordanceBuffer = new(64);

	private BotProfile _profile = BotProfile.CreateDefault();
	private float[] _weights = Array.Empty<float>();
	private float _decisionInterval = 1.0f;
	private float _aggressionMultiplier = 1.0f;
	private float _actionTemperature = 0.0f;
	private float _timer = 0.0f;
	private int _playerIndex;

	public int PlayerIndex => _playerIndex;
	public float[] Weights => _weights;
	public float DecisionInterval => _decisionInterval;
	public float AggressionMultiplier => _aggressionMultiplier;
	public float ActionTemperature => _actionTemperature;
	public BotProfile Profile => _profile;

	public BotController(int playerIndex = 0, BotProfile? profile = null)
	{
		_playerIndex = playerIndex;
		LoadProfile(profile ?? BotProfile.CreateDefault());
	}

	public void LoadProfile(BotProfile profile)
	{
		if (profile == null)
		{
			return;
		}

		_profile = profile;
		_decisionInterval = profile.DecisionIntervalSeconds > 0.05f ? profile.DecisionIntervalSeconds : 1.0f;
		_aggressionMultiplier = profile.AggressionMultiplier > 0.0f ? profile.AggressionMultiplier : 1.0f;
		_actionTemperature = Math.Clamp(profile.ActionTemperature, 0.0f, 2.0f);

		if (profile.Weights != null && profile.Weights.Length >= AffordanceScanner.FeatureCount)
		{
			_weights = (float[])profile.Weights.Clone();
		}
		else
		{
			_weights = new float[AffordanceScanner.FeatureCount]
			{
				-0.5f,
				0.8f,
				0.2f,
				0.5f,
				0.7f,
				0.9f,
				0.4f,
				0.6f
			};
		}
	}

	public void Tick(World world, int playerIndex, float delta)
	{
		_playerIndex = playerIndex;
		_timer += delta;
		if (_timer < _decisionInterval)
		{
			return;
		}
		_timer = 0.0f;

		_scanner.ScanAffordances(world, playerIndex, _affordanceBuffer);
		var action = _policy.SelectAction(_affordanceBuffer, _weights, 0.0f, _actionTemperature, _aggressionMultiplier);

		if (action.HasValue)
		{
			ExecuteAction(world, action.Value);
		}
	}

	private void ExecuteAction(World world, in GenericAffordance aff)
	{
		if (!world.IsAlive(aff.SourceEntity)) return;

		switch (aff.Intent)
		{
			case CommandIntent.Attack:
				if (world.IsAlive(aff.TargetEntity))
				{
					world.AddOrGet(aff.SourceEntity, new AttackTarget(aff.TargetEntity));
				}
				break;

			case CommandIntent.MoveTo:
				world.AddOrGet(aff.SourceEntity, new MoveTo(aff.TargetPosition));
				break;

			case CommandIntent.Train:
				if (world.Has<ProductionQueue>(aff.SourceEntity))
				{
					ref var q = ref world.Get<ProductionQueue>(aff.SourceEntity);
					if (q.UnitIds.Count < 5)
					{
						q.UnitIds.Add("grunt");
					}
				}
				break;
		}
	}
}
