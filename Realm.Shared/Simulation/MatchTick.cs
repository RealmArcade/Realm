using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Stats;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;

namespace Realm.Shared.Simulation;

public class MatchTick
{
	private const float DayNightCycleSeconds = 90f;
	private const int TimeOfDaySlotCount = 4;

	private static readonly QueryDescription BuffQuery = QueryCache.AllBuffsNoneDeadQuery;
	private static readonly QueryDescription BuffStateQuery = QueryCache.AllBuffStateNoneDeadQuery;
	private static readonly QueryDescription AbsorbQuery = new QueryDescription().WithAll<AbsorbShield>();
	private static readonly QueryDescription SummonQuery = new QueryDescription().WithAll<SummonedUnit>().WithNone<Dead>();
	private static readonly QueryDescription StatsQuery = new QueryDescription().WithAll<Stats>().WithNone<Dead>();
	private static readonly QueryDescription AttackQuery = QueryCache.AllAttackQuery;
	private static readonly QueryDescription HealthQuery = QueryCache.AllHealthNoneDeadQuery;
	private static readonly QueryDescription ManaQuery = QueryCache.AllManaAndManaRegenNoneDeadQuery;

	private readonly SimulationContext _simulation;
	private readonly EntityCallback<Buffs> _buffsDelegate;
	private readonly EntityCallback<BuffState> _buffStateDelegate;
	private readonly EntityCallback<AbsorbShield> _absorbDelegate;
	private readonly EntityCallback<SummonedUnit> _summonDelegate;
	private readonly EntityCallback<Stats> _statsDelegate;
	private readonly EntityCallback<Cooldowns> _cooldownsDelegate;
	private readonly EntityCallback<SpellCooldowns> _spellCooldownsDelegate;
	private readonly EntityCallback<Attack> _attackDelegate;
	private readonly EntityCallback<Health> _healthDelegate;
	private readonly EntityCallback<Mana, ManaRegen> _manaDelegate;

	private float _deltaSeconds;
	private float _shroudTimer;

	public MatchTick(SimulationContext simulation)
	{
		_simulation = simulation;
		_buffsDelegate = AdvanceBuffs;
		_buffStateDelegate = AdvanceBuffStates;
		_absorbDelegate = AdvanceAbsorbPools;
		_summonDelegate = AdvanceSummonLifespans;
		_statsDelegate = AdvanceTimedModifiers;
		_cooldownsDelegate = AdvanceCooldowns;
		_spellCooldownsDelegate = AdvanceSpellCooldowns;
		_attackDelegate = AdvanceAttackCooldown;
		_healthDelegate = AdvanceHealthRegeneration;
		_manaDelegate = AdvanceManaRegeneration;
	}

	public EcsWorld World => _simulation.World;

	public bool UnlimitedPower { get; set; }

	public float ShroudInterval { get; set; } = ShroudSimulationService.UpdateInterval;

	public event Action<Entity>? SummonExpired;

	public void AdvanceCooldowns(float deltaSeconds)
	{
		_deltaSeconds = deltaSeconds;
		AdvanceMatchClock(deltaSeconds);
		World.Query(in QueryCache.AllSpellCooldownsQuery, _spellCooldownsDelegate);
		World.Query(in QueryCache.AllCooldownsQuery, _cooldownsDelegate);
	}

	public void AdvanceStatusEffects(float deltaSeconds)
	{
		_deltaSeconds = deltaSeconds;

		if (!_simulation.ShroudDisabled)
		{
			_shroudTimer += deltaSeconds;
			if (_shroudTimer >= ShroudInterval)
			{
				_shroudTimer = 0f;
				ShroudSimulationService.Tick(World, _simulation.WorldEntity);
			}
		}

		World.Query(in BuffQuery, _buffsDelegate);
		World.Query(in BuffStateQuery, _buffStateDelegate);
		World.Query(in StatsQuery, _statsDelegate);
		World.Query(in AbsorbQuery, _absorbDelegate);
		World.Query(in SummonQuery, _summonDelegate);
	}

	public void AdvanceRegeneration(float deltaSeconds)
	{
		_deltaSeconds = deltaSeconds;
		World.Query(in AttackQuery, _attackDelegate);
		World.Query(in HealthQuery, _healthDelegate);
		World.Query(in ManaQuery, _manaDelegate);
	}

	private void AdvanceMatchClock(float deltaSeconds)
	{
		Entity worldEntity = _simulation.WorldEntity;
		if (worldEntity == Entity.Null || !World.IsAlive(worldEntity)) return;

		if (World.Has<WorldState>(worldEntity))
		{
			var state = World.Get<WorldState>(worldEntity);
			float elapsed = state.GameElapsedTime + deltaSeconds;
			float timer = state.TimeOfDayTimer;
			int index = state.TimeOfDayIndex;

			if (state.DayNightCycleEnabled)
			{
				timer += deltaSeconds;
				if (timer >= DayNightCycleSeconds) timer -= DayNightCycleSeconds;

				index = (int)(timer / DayNightCycleSeconds * TimeOfDaySlotCount) % TimeOfDaySlotCount;
			}

			World.Set(worldEntity, new WorldState(elapsed, index, timer, state.DayNightCycleEnabled));
		}

		if (!World.Has<CountdownState>(worldEntity)) return;

		var countdown = World.Get<CountdownState>(worldEntity);
		if (!countdown.Active) return;

		float remaining = countdown.Duration - deltaSeconds;
		World.Set(worldEntity, remaining <= 0f
			? new CountdownState(false, 0f, countdown.Text)
			: new CountdownState(true, remaining, countdown.Text));
	}

	private void AdvanceCooldowns(Entity entity, ref Cooldowns cooldowns)
	{
		if (UnlimitedPower)
		{
			cooldowns.Clear();
			return;
		}

		cooldowns.Advance(_deltaSeconds);
	}

	private void AdvanceSpellCooldowns(Entity entity, ref SpellCooldowns spellCooldowns)
	{
		if (UnlimitedPower)
		{
			spellCooldowns.Clear();
			return;
		}

		spellCooldowns.Advance(_deltaSeconds);
	}

	private void AdvanceBuffs(Entity entity, ref Buffs buffs) => buffs.Advance(_deltaSeconds);

	private void AdvanceBuffStates(Entity entity, ref BuffState buffs) => buffs.Advance(_deltaSeconds);

	private void AdvanceAbsorbPools(Entity entity, ref AbsorbShield shield)
	{
		shield.Advance(_deltaSeconds);

		if (shield.Count == 0) World.Remove<AbsorbShield>(entity);
	}

	private void AdvanceSummonLifespans(Entity entity, ref SummonedUnit summon)
	{
		if (summon.Remaining <= 0f) return;

		summon.Remaining -= _deltaSeconds;
		if (summon.Remaining > 0f) return;

		summon.Remaining = 0f;
		SummonExpired?.Invoke(entity);
	}

	private void AdvanceTimedModifiers(Entity entity, ref Stats stats)
	{
		if (!World.Has<ModifierState>(entity)) return;

		ref var modifiers = ref World.Get<ModifierState>(entity);
		if (!modifiers.Advance(_deltaSeconds)) return;

		AttributeStatCalculator.RecalculateEntityStats(World, entity);
	}

	private void AdvanceAttackCooldown(Entity entity, ref Attack attack)
	{
		if (UnlimitedPower)
		{
			attack.CurrentCooldown = 0f;
			return;
		}

		if (attack.CurrentCooldown > 0f) attack.CurrentCooldown = MathF.Max(0f, attack.CurrentCooldown - _deltaSeconds);
	}

	private void AdvanceHealthRegeneration(Entity entity, ref Health health)
	{
		health.TimeSinceLastDamage += _deltaSeconds;
		if (health.HpRegen <= 0f || health.Current >= health.Max) return;
		if (health.HpRegenCombatDelay > 0f && health.TimeSinceLastDamage < health.HpRegenCombatDelay) return;

		health.Current = MathF.Min(health.Max, health.Current + health.HpRegen * _deltaSeconds);
	}

	private void AdvanceManaRegeneration(Entity entity, ref Mana mana, ref ManaRegen regen)
	{
		if (regen.PerSecond <= 0f || mana.Current >= mana.Max) return;

		mana.Current = MathF.Min(mana.Max, mana.Current + regen.PerSecond * _deltaSeconds);
	}
}
