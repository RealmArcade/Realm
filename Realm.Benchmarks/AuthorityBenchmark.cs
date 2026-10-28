using System.Diagnostics;
using System.Numerics;
using MemoryPack;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.ListenServer;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.Benchmarks;

public sealed class AuthorityBenchmark : IDisposable
{
	private const int MapCellCount = 128;
	private const float MapQuadSize = 2f;
	private const float MapCellSize = 0.5f;
	private const float UnitSpeedMetersPerSecond = 5f;
	private const float UnitAcceleration = 20f;
	private const float UnitTurnRate = 10f;
	private const float UnitHealth = 1_000_000f;
	private const float UnitArmor = 5f;
	private const float AttackDamage = 12f;
	private const float AttackRange = 2f;
	private const float AttackCooldownSeconds = 1.2f;
	private const float ScanRadiusMeters = 12f;
	private const int ReissueOrderIntervalTicks = 10;
	private const int PopulationRecomputeIntervalTicks = 5;
	private const int SnapshotSampleIntervalTicks = 10;
	private const int BaselineSequence = 1;

	private readonly BenchmarkOptions _options;
	private readonly SimulationContext _context = new();
	private readonly MatchTick _matchTick;
	private readonly MovementAndPathfindingService _movement;
	private readonly CombatAndDamageService _combat;
	private readonly ResourceEconomyService _economy;
	private readonly List<Entity> _units = new();
	private readonly Entity[] _playerEntities;
	private readonly Entity _worldEntity;
	private readonly AuthoritativeSnapshotBuilder _snapshots;
	private readonly Random _random = new(20261028);

	private int _sequence = BaselineSequence;
	private bool _disposed;

	public AuthorityBenchmark(BenchmarkOptions options)
	{
		_options = options;
		_playerEntities = new Entity[EcsWorld.MAX_PLAYERS];

		_worldEntity = CreateWorld();
		_playerEntities = CreatePlayers();
		SpawnUnits();

		_matchTick = new MatchTick(_context);
		_movement = _context.MovementPathfindingService;
		_combat = _context.CombatDamageService;
		_economy = _context.ResourceEconomyService;

		_economy.SetRuntimeReferences(_context.DefinitionManager, new ResourceId("gold"), new ResourceId("wood"), new ResourceId("stone"));
		_combat.EnemyPredicate = (playerIndex, otherPlayerIndex) => TeamOf(playerIndex) != TeamOf(otherPlayerIndex);
		_combat.UnitWeaponsProvider = _ => null;
		_combat.ItemGrantedWeaponsProvider = _ => null;
		_combat.UnlimitedPowerProvider = () => false;
		_combat.OnKillUnitRequested = _ => { };

		_snapshots = new AuthoritativeSnapshotBuilder(_context.World, _context, _worldEntity, _context.MapRules, PeerIdForPlayerSlot);
	}

	private static int TeamOf(int playerIndex) => playerIndex < EcsWorld.MAX_PLAYERS / 2 ? 0 : 1;

	private static int PeerIdForPlayerSlot(int playerSlot) => playerSlot + 1;

	private Entity CreateWorld()
	{
		var heights = new float[MapCellCount, MapCellCount];
		var pathingCodes = new int[MapCellCount, MapCellCount];
		int walkable = (int)(TerrainPathingFlags.Ground | TerrainPathingFlags.Flying);
		for (int z = 0; z < MapCellCount; z++)
		{
			for (int x = 0; x < MapCellCount; x++) pathingCodes[x, z] = walkable;
		}

		Entity worldEntity = _context.World.Create(new TerrainState(MapCellCount, MapCellCount, MapQuadSize, MapCellSize, heights, pathingCodes));
		_context.WorldEntity = worldEntity;
		_context.TerrainNavMeshService.BakeNavMesh(ref _context.World.Get<TerrainState>(worldEntity));

		_context.World.Add(worldEntity, new WorldState(0f, 0, 0f, false));
		_context.World.Add(worldEntity, new NetworkMappingState(new Dictionary<int, Entity>(), new Dictionary<int, int>(), new Dictionary<int, Entity>()));
		return worldEntity;
	}

	private Entity[] CreatePlayers()
	{
		ref NetworkMappingState mapping = ref _context.World.Get<NetworkMappingState>(_worldEntity);
		Entity[] players = mapping.PlayerEntities;

		for (int slot = 0; slot < _options.Players; slot++)
		{
			Entity playerEntity = _context.World.Create(new DefinitionId("bench_player"), new UnitOwnerPlayer(slot));
			players[slot] = playerEntity;
		}

		return players;
	}

	private void SpawnUnits()
	{
		float halfExtent = MapCellCount * MapQuadSize * 0.5f;

		for (int index = 0; index < _options.Units; index++)
		{
			int playerSlot = index % _options.Players;
			Entity playerEntity = _playerEntities[playerSlot];
			int team = TeamOf(playerSlot);

			float spawnX = team == 0 ? -halfExtent * 0.75f + (float)_random.NextDouble() * halfExtent * 0.35f : halfExtent * 0.4f + (float)_random.NextDouble() * halfExtent * 0.35f;
			float spawnZ = -halfExtent * 0.9f + (float)_random.NextDouble() * halfExtent * 1.8f;

			Entity unit = _context.World.Create(
				new Position(new Vector3(spawnX, 0f, spawnZ)),
				new Health(UnitHealth, UnitHealth),
				new Resistances(UnitArmor),
				new Attack(AttackDamage, AttackRange, AttackCooldownSeconds),
				new CombatTargeting(false, true),
				new ScanRadius(ScanRadiusMeters),
				new MovementStats(UnitSpeedMetersPerSecond, UnitAcceleration, UnitTurnRate),
				new PathingFlags((int)TerrainPathingFlags.Ground),
				new Movable(),
				new CollisionScale(1f),
				new DefinitionId("bench_soldier"),
				new UnitOwnerPlayer(playerSlot));

			_context.World.Add(unit, new Owner(new PlayerEntity(playerEntity)));
			_context.World.Add(unit, new MoveTo(TargetForTeam(team)));
			_units.Add(unit);
		}
	}

	private Vector3 TargetForTeam(int team)
	{
		float halfExtent = MapCellCount * MapQuadSize * 0.5f;
		float targetX = team == 0 ? halfExtent * 0.45f + (float)_random.NextDouble() * halfExtent * 0.4f : -halfExtent * 0.85f + (float)_random.NextDouble() * halfExtent * 0.4f;
		float targetZ = -halfExtent * 0.9f + (float)_random.NextDouble() * halfExtent * 1.8f;
		return new Vector3(targetX, 0f, targetZ);
	}

	private void StepOnce()
	{
		float deltaSeconds = (float)_options.TickDeltaSeconds;
		_matchTick.AdvanceCooldowns(deltaSeconds);
		_moveUnits(deltaSeconds);
		_combat.StepCombat(deltaSeconds, _movement);
		_economy.StepEconomy(deltaSeconds);
		_matchTick.AdvanceStatusEffects(deltaSeconds);
		_matchTick.AdvanceRegeneration(deltaSeconds);
	}

	private void _moveUnits(float deltaSeconds)
	{
		_movement.StepMovement(deltaSeconds);

		if (_sequence % PopulationRecomputeIntervalTicks == 0) _economy.RecomputeCurrentPopulation();
		if (_sequence % ReissueOrderIntervalTicks != 0) return;

		for (int index = 0; index < _units.Count; index++)
		{
			Entity unit = _units[index];
			if (!_context.World.IsAlive(unit)) continue;
			if (_context.World.Has<MoveTo>(unit)) continue;
			if (!_context.World.TryGet<UnitOwnerPlayer>(unit, out UnitOwnerPlayer owner)) continue;

			_context.World.Add(unit, new MoveTo(TargetForTeam(TeamOf(owner.Value))));
		}
	}

	public BenchmarkResult Run()
	{
		for (int tick = 0; tick < _options.WarmupTicks; tick++)
		{
			StepOnce();
			_sequence++;
		}

		var tickMicros = new double[_options.Ticks];
		double cooldownMicros = 0;
		double movementMicros = 0;
		double combatMicros = 0;
		double economyMicros = 0;
		double statusMicros = 0;
		double snapshotBuildMicros = 0;
		double snapshotSerializeMicros = 0;
		double snapshotCompressMicros = 0;
		double deltaBytes = 0;
		double baselineBytes = 0;
		double visibleUnits = 0;
		int snapshotPasses = 0;
		int deltaPasses = 0;
		bool baselineTaken = false;

		float deltaSeconds = (float)_options.TickDeltaSeconds;

		for (int tick = 0; tick < _options.Ticks; tick++)
		{
			long tickStart = Stopwatch.GetTimestamp();

			long phaseStart = Stopwatch.GetTimestamp();
			_matchTick.AdvanceCooldowns(deltaSeconds);
			cooldownMicros += Elapsed(phaseStart);

			phaseStart = Stopwatch.GetTimestamp();
			_moveUnits(deltaSeconds);
			movementMicros += Elapsed(phaseStart);

			phaseStart = Stopwatch.GetTimestamp();
			_combat.StepCombat(deltaSeconds, _movement);
			combatMicros += Elapsed(phaseStart);

			phaseStart = Stopwatch.GetTimestamp();
			_economy.StepEconomy(deltaSeconds);
			economyMicros += Elapsed(phaseStart);

			phaseStart = Stopwatch.GetTimestamp();
			_matchTick.AdvanceStatusEffects(deltaSeconds);
			_matchTick.AdvanceRegeneration(deltaSeconds);
			statusMicros += Elapsed(phaseStart);

			tickMicros[tick] = Elapsed(tickStart);
			_sequence++;

			if (_sequence % SnapshotSampleIntervalTicks != 0) continue;

			bool forceBaseline = !baselineTaken;
			for (int slot = 0; slot < _options.Players; slot++)
			{
				phaseStart = Stopwatch.GetTimestamp();
				WorldSnapshot snapshot = _snapshots.Build(PeerIdForPlayerSlot(slot), _sequence, new PlayerEntity(_playerEntities[slot]), forceBaseline);
				snapshotBuildMicros += Elapsed(phaseStart);
				visibleUnits += snapshot.Units.Count;

				phaseStart = Stopwatch.GetTimestamp();
				byte[] serialized = MemoryPackSerializer.Serialize(snapshot);
				snapshotSerializeMicros += Elapsed(phaseStart);

				phaseStart = Stopwatch.GetTimestamp();
				byte[] frame = snapshot.IsBaseline ? SnapshotWireFormat.EncodeBaseline(serialized) : SnapshotWireFormat.EncodeDelta(serialized);
				snapshotCompressMicros += Elapsed(phaseStart);

				if (snapshot.IsBaseline) baselineBytes += frame.Length;
				else
				{
					deltaBytes += frame.Length;
					deltaPasses++;
				}
			}

			snapshotPasses++;
			baselineTaken = true;
		}

		double allVisibleDeltaBytes = MeasureAllVisibleDelta();

		Array.Sort(tickMicros);

		return new BenchmarkResult(
			_options.Units,
			_options.Players,
			_options.Ticks,
			_options.TickDeltaSeconds,
			_options.ReferenceDerate,
			Mean(tickMicros),
			Percentile(tickMicros, 0.50),
			Percentile(tickMicros, 0.95),
			Percentile(tickMicros, 0.99),
			tickMicros[^1],
			cooldownMicros / _options.Ticks,
			movementMicros / _options.Ticks,
			combatMicros / _options.Ticks,
			economyMicros / _options.Ticks,
			statusMicros / _options.Ticks,
			snapshotPasses == 0 ? 0 : snapshotBuildMicros / (snapshotPasses * (double)_options.Players),
			snapshotPasses == 0 ? 0 : snapshotSerializeMicros / (snapshotPasses * (double)_options.Players),
			snapshotPasses == 0 ? 0 : snapshotCompressMicros / (snapshotPasses * (double)_options.Players),
			deltaPasses == 0 ? 0 : deltaBytes / deltaPasses,
			snapshotPasses == 0 ? 0 : baselineBytes / _options.Players,
			allVisibleDeltaBytes,
			snapshotPasses == 0 ? 0 : visibleUnits / (snapshotPasses * (double)_options.Players));
	}

	private double MeasureAllVisibleDelta()
	{
		_context.ShroudDisabled = true;

		for (int slot = 0; slot < _options.Players; slot++) _snapshots.Build(PeerIdForPlayerSlot(slot), ++_sequence, new PlayerEntity(_playerEntities[slot]), true);

		double bytes = 0;
		for (int slot = 0; slot < _options.Players; slot++)
		{
			WorldSnapshot snapshot = _snapshots.Build(PeerIdForPlayerSlot(slot), _sequence + 1, new PlayerEntity(_playerEntities[slot]), false);
			byte[] frame = SnapshotWireFormat.EncodeDelta(MemoryPackSerializer.Serialize(snapshot));
			bytes += frame.Length;
		}

		_context.ShroudDisabled = false;
		return bytes / _options.Players;
	}

	private static double Elapsed(long startTimestamp) => (Stopwatch.GetTimestamp() - startTimestamp) * 1_000_000.0 / Stopwatch.Frequency;

	private static double Mean(double[] values)
	{
		double total = 0;
		for (int index = 0; index < values.Length; index++) total += values[index];
		return values.Length == 0 ? 0 : total / values.Length;
	}

	private static double Percentile(double[] sortedValues, double percentile)
	{
		if (sortedValues.Length == 0) return 0;
		int index = (int)Math.Clamp(Math.Round(percentile * (sortedValues.Length - 1)), 0, sortedValues.Length - 1);
		return sortedValues[index];
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_context.Dispose();
	}
}
