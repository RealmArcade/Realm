using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System.Numerics;

namespace Realm.Ecs.AI.Simulation;

public delegate int? WinConditionEvaluator(World world, float matchDurationSeconds);
public delegate void MapSimulationInitializer(World world);

public class SimulationMatchResult
{
	public int WinnerPlayerIndex { get; set; } = -1;
	public int TotalTicksExecuted { get; set; }
	public float MatchDurationSeconds { get; set; }
	public int Player0UnitsBuilt { get; set; }
	public int Player1UnitsBuilt { get; set; }
}

public class HeadlessSimulationRunner
{
	private static readonly QueryDescription MoveQuery = new QueryDescription().WithAll<Position, MoveTo>().WithNone<Dead>();
	private static readonly QueryDescription AttackQuery = new QueryDescription().WithAll<Position, AttackTarget, Attack>().WithNone<Dead>();
	private static readonly QueryDescription ProductionQuery = new QueryDescription().WithAll<ProductionQueue, UnitOwnerPlayer, Position>().WithNone<Dead>();
	private static readonly QueryDescription AliveUnitsQuery = new QueryDescription().WithAll<UnitOwnerPlayer, Health>().WithNone<Dead>();

	private World _world = null!;
	private readonly AffordanceScanner _scanner = new();
	private readonly LinearUtilityPolicy _policy = new();
	private readonly List<GenericAffordance> _affordanceBuffer = new(64);
	private readonly List<Entity> _deadEntitiesBuffer = new(32);

	public World World => _world;

	public HeadlessSimulationRunner()
	{
		ResetGame();
	}

	public void ResetGame(MapSimulationInitializer? customInitializer = null)
	{
		_world?.Dispose();
		_world = World.Create();

		if (customInitializer != null)
		{
			customInitializer(_world);
		}
		else
		{
			SpawnDefaultGame();
		}
	}

	private void SpawnDefaultGame()
	{
		var p0 = _world.Create(
			new Player(),
			new UnitOwnerPlayer(0),
			new PlayerResources(new Dictionary<Common.ResourceId, int> { { new Common.ResourceId("Gold"), 1000 } })
		);

		var p1 = _world.Create(
			new Player(),
			new UnitOwnerPlayer(1),
			new PlayerResources(new Dictionary<Common.ResourceId, int> { { new Common.ResourceId("Gold"), 1000 } })
		);

		SpawnStartingBase(0, new Vector3(-20, 0, 0));
		SpawnStartingBase(1, new Vector3(20, 0, 0));
	}

	private void SpawnStartingBase(int playerIndex, Vector3 pos)
	{
		var baseEntity = _world.Create(
			new Position(pos),
			new Health(1000f, 1000f),
			new UnitOwnerPlayer(playerIndex),
			new ProductionQueue()
		);

		for (int i = 0; i < 3; i++)
		{
			var unitEntity = _world.Create(
				new Position(pos + new Vector3((i - 1) * 2f, 0, 3f)),
				new Health(100f, 100f),
				new Attack(15f, 3f, 1f),
				new MovementStats(5f, 10f, 10f),
				new UnitOwnerPlayer(playerIndex)
			);
		}
	}

	public void Tick(float delta, float[] p0Weights, float[] p1Weights, float p0Temperature = 0.0f, float p1Temperature = 0.0f, float epsilon = 0.0f)
	{
		ExecuteAgentDecisions(0, p0Weights, p0Temperature, epsilon);
		ExecuteAgentDecisions(1, p1Weights, p1Temperature, epsilon);

		StepMovement(delta);
		StepCombat(delta);
		StepProduction(delta);
	}

	private void ExecuteAgentDecisions(int playerIndex, float[] weights, float temperature, float epsilon)
	{
		if (weights == null || weights.Length == 0) return;

		_scanner.ScanAffordances(_world, playerIndex, _affordanceBuffer);
		var action = _policy.SelectAction(_affordanceBuffer, weights, epsilon, temperature);

		if (action.HasValue)
		{
			var aff = action.Value;
			if (aff.Intent == CommandIntent.Attack && _world.IsAlive(aff.TargetEntity))
			{
				_world.AddOrGet(aff.SourceEntity, new AttackTarget(aff.TargetEntity));
			}
			else if (aff.Intent == CommandIntent.MoveTo)
			{
				_world.AddOrGet(aff.SourceEntity, new MoveTo(aff.TargetPosition));
			}
			else if (aff.Intent == CommandIntent.Train)
			{
				if (_world.Has<ProductionQueue>(aff.SourceEntity))
				{
					ref var q = ref _world.Get<ProductionQueue>(aff.SourceEntity);
					if (q.UnitIds.Count < 5)
					{
						q.UnitIds.Add("grunt");
					}
				}
			}
		}
	}

	private void StepMovement(float delta)
	{
		_world.Query(in MoveQuery, (Entity e, ref Position pos, ref MoveTo move) =>
		{
			Vector3 dir = move.Target - pos.Value;
			float dist = dir.Length();
			if (dist < 0.5f)
			{
				_world.Remove<MoveTo>(e);
			}
			else
			{
				float speed = _world.Has<MovementStats>(e) ? _world.Get<MovementStats>(e).Speed : 5.0f;
				pos.Value += Vector3.Normalize(dir) * MathF.Min(dist, speed * delta);
			}
		});
	}

	private void StepCombat(float delta)
	{
		_deadEntitiesBuffer.Clear();

		_world.Query(in AttackQuery, (Entity attacker, ref Position aPos, ref AttackTarget target, ref Attack attack) =>
		{
			if (!_world.IsAlive(target.Target) || _world.Has<Dead>(target.Target))
			{
				_world.Remove<AttackTarget>(attacker);
				return;
			}

			if (_world.Has<Position>(target.Target) && _world.Has<Health>(target.Target))
			{
				var tPos = _world.Get<Position>(target.Target).Value;
				float dist = Vector3.Distance(aPos.Value, tPos);
				if (dist <= 3.0f)
				{
					ref var hp = ref _world.Get<Health>(target.Target);
					hp.Current -= attack.Damage * delta;
					if (hp.Current <= 0.0f)
					{
						_deadEntitiesBuffer.Add(target.Target);
					}
				}
				else
				{
					_world.AddOrGet(attacker, new MoveTo(tPos));
				}
			}
		});

		for (int i = 0; i < _deadEntitiesBuffer.Count; i++)
		{
			var d = _deadEntitiesBuffer[i];
			if (_world.IsAlive(d))
			{
				_world.AddOrGet(d, new Dead());
				_world.Destroy(d);
			}
		}
	}

	private void StepProduction(float delta)
	{
		_world.Query(in ProductionQuery, (Entity bld, ref ProductionQueue q, ref UnitOwnerPlayer owner, ref Position pos) =>
		{
			if (q.UnitIds.Count > 0)
			{
				q.CurrentProgress += delta;
				if (q.CurrentProgress >= q.BuildTime)
				{
					q.CurrentProgress = 0.0f;
					q.UnitIds.RemoveAt(0);

					var spawnedUnit = _world.Create(
						new Position(pos.Value + new Vector3(0, 0, 3f)),
						new Health(100f, 100f),
						new Attack(15f, 3f, 1f),
						new MovementStats(5f, 10f, 10f),
						new UnitOwnerPlayer(owner.PlayerIndex)
					);
				}
			}
		});
	}

	public SimulationMatchResult RunMatch(
		float[] p0Weights,
		float[] p1Weights,
		int maxTicks = 1000,
		float fixedDelta = 0.1f,
		WinConditionEvaluator? winConditionEvaluator = null,
		MapSimulationInitializer? customInitializer = null,
		float p0Temperature = 0.0f,
		float p1Temperature = 0.0f)
	{
		ResetGame(customInitializer);

		for (int tick = 0; tick < maxTicks; tick++)
		{
			Tick(fixedDelta, p0Weights, p1Weights, p0Temperature, p1Temperature);

			float currentDuration = (tick + 1) * fixedDelta;
			if (winConditionEvaluator != null)
			{
				int? customWinner = winConditionEvaluator(_world, currentDuration);
				if (customWinner.HasValue)
				{
					return new SimulationMatchResult
					{
						WinnerPlayerIndex = customWinner.Value,
						TotalTicksExecuted = tick + 1,
						MatchDurationSeconds = currentDuration,
						Player0UnitsBuilt = 0,
						Player1UnitsBuilt = 0
					};
				}
			}
			else
			{
				int p0Alive = CountAliveUnits(0);
				int p1Alive = CountAliveUnits(1);

				if (p0Alive == 0 || p1Alive == 0)
				{
					int winner = p0Alive > 0 ? 0 : (p1Alive > 0 ? 1 : -1);
					return new SimulationMatchResult
					{
						WinnerPlayerIndex = winner,
						TotalTicksExecuted = tick + 1,
						MatchDurationSeconds = currentDuration,
						Player0UnitsBuilt = 0,
						Player1UnitsBuilt = 0
					};
				}
			}
		}

		int finalP0Count = CountAliveUnits(0);
		int finalP1Count = CountAliveUnits(1);
		int finalWinner = finalP0Count > finalP1Count ? 0 : (finalP1Count > finalP0Count ? 1 : -1);

		return new SimulationMatchResult
		{
			WinnerPlayerIndex = finalWinner,
			TotalTicksExecuted = maxTicks,
			MatchDurationSeconds = maxTicks * fixedDelta,
			Player0UnitsBuilt = 0,
			Player1UnitsBuilt = 0
		};
	}

	public int CountAliveUnits(int playerIndex)
	{
		int count = 0;
		_world.Query(in AliveUnitsQuery, (Entity e, ref UnitOwnerPlayer owner) =>
		{
			if (owner.PlayerIndex == playerIndex)
			{
				count++;
			}
		});
		return count;
	}
}
