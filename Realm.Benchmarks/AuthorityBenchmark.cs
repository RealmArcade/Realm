using System.Buffers.Binary;
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
using ZstdSharp;

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
	private readonly SnapshotFanOut _snapshots;
	private readonly double[] _peerBuildMicros;
	private readonly double[] _peerSerializeMicros;
	private readonly double[] _peerCompressMicros;
	private readonly int[] _peerFrameBytes;
	private readonly int[] _peerVisibleUnits;
	private readonly bool[] _peerIsBaseline;
	private readonly ParallelOptions _parallelOptions = new();
	private readonly Action<int> _describeAndFrameOnePeer;
	private bool _measuredForceBaseline;
	private readonly Random _random = new(20261028);

	private int _sequence = BaselineSequence;
	private bool _disposed;

	public AuthorityBenchmark(BenchmarkOptions options)
	{
		_options = options;
		_playerEntities = new Entity[EcsWorld.MAX_PLAYERS];
		_peerBuildMicros = new double[options.Players];
		_peerSerializeMicros = new double[options.Players];
		_peerCompressMicros = new double[options.Players];
		_peerFrameBytes = new int[options.Players];
		_peerVisibleUnits = new int[options.Players];
		_peerIsBaseline = new bool[options.Players];
		_describeAndFrameOnePeer = DescribeAndFrameOnePeer;

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

		_snapshots = new SnapshotFanOut(_context.World, _context, _worldEntity, _context.MapRules, options.Workers > 0 ? options.Workers : SnapshotFanOut.DefaultWorkerCount);
		_snapshots.PrepareFor(PeerIdByPlayerSlot());
		_parallelOptions.MaxDegreeOfParallelism = Math.Min(options.Players, _snapshots.WorkerCount);
	}

	private static int TeamOf(int playerIndex) => playerIndex < EcsWorld.MAX_PLAYERS / 2 ? 0 : 1;

	private int[] PeerIdByPlayerSlot()
	{
		var peerIds = new int[EcsWorld.MAX_PLAYERS];
		for (int slot = 0; slot < _options.Players; slot++) peerIds[slot] = slot + 1;
		return peerIds;
	}

	private static int PeerIdForPlayerSlot(int playerSlot) => playerSlot + 1;

	private WorldSnapshot DescribePeer(int playerSlot, int sequence, bool forceBaseline)
	{
		AuthoritativeSnapshotBuilder builder = _snapshots.Rent();
		try
		{
			return builder.Build(PeerIdForPlayerSlot(playerSlot), sequence, new PlayerEntity(_playerEntities[playerSlot]), forceBaseline);
		}
		finally
		{
			_snapshots.Return(builder);
		}
	}

	private void DescribeAndFrameOnePeer(int playerSlot)
	{
		long start = Stopwatch.GetTimestamp();
		WorldSnapshot snapshot = DescribePeer(playerSlot, _sequence, _measuredForceBaseline);
		_peerBuildMicros[playerSlot] = Elapsed(start);
		_peerVisibleUnits[playerSlot] = snapshot.Units.Count;
		_peerIsBaseline[playerSlot] = snapshot.IsBaseline;

		start = Stopwatch.GetTimestamp();
		byte[] serialized = MemoryPackSerializer.Serialize(snapshot);
		_peerSerializeMicros[playerSlot] = Elapsed(start);

		start = Stopwatch.GetTimestamp();
		byte[] frame = snapshot.IsBaseline ? SnapshotWireFormat.EncodeBaseline(serialized) : SnapshotWireFormat.EncodeDelta(serialized);
		_peerCompressMicros[playerSlot] = Elapsed(start);
		_peerFrameBytes[playerSlot] = frame.Length;
	}

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
		double snapshotPhaseMicros = 0;
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

			// The authority describes every peer of a room at once, so the thing worth measuring is the whole phase on
			// the wall clock, not the sum of the descriptions a single thread would have made.
			_measuredForceBaseline = !baselineTaken;
			long snapshotStart = Stopwatch.GetTimestamp();
			Parallel.For(0, _options.Players, _parallelOptions, _describeAndFrameOnePeer);
			snapshotPhaseMicros += Elapsed(snapshotStart);

			for (int slot = 0; slot < _options.Players; slot++)
			{
				snapshotBuildMicros += _peerBuildMicros[slot];
				snapshotSerializeMicros += _peerSerializeMicros[slot];
				snapshotCompressMicros += _peerCompressMicros[slot];
				visibleUnits += _peerVisibleUnits[slot];

				if (_peerIsBaseline[slot]) baselineBytes += _peerFrameBytes[slot];
				else
				{
					deltaBytes += _peerFrameBytes[slot];
					deltaPasses++;
				}
			}

			snapshotPasses++;
			baselineTaken = true;
		}

		double allVisibleDeltaBytes = MeasureAllVisibleDelta();
		DeltaComposition composition = MeasureComposition();

		Array.Sort(tickMicros);

		return new BenchmarkResult(
			_options.Units,
			_options.Players,
			_snapshots.WorkerCount,
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
			snapshotPasses == 0 ? 0 : snapshotPhaseMicros / snapshotPasses,
			deltaPasses == 0 ? 0 : deltaBytes / deltaPasses,
			snapshotPasses == 0 ? 0 : baselineBytes / _options.Players,
			allVisibleDeltaBytes,
			snapshotPasses == 0 ? 0 : visibleUnits / (snapshotPasses * (double)_options.Players),
			composition);
	}

	/// <summary>
	///     Frames one delta stream at every level worth considering, so the encoder's level is chosen from what this room
	///     actually compresses rather than from a rule of thumb. The frames are encoded in sequence through one encoder
	///     each: a compressor carries state between the frames it produces, and a stream is what a peer receives.
	/// </summary>
	public ZstdSweep MeasureZstdSweep(int frameCount)
	{
		for (int slot = 0; slot < _options.Players; slot++) DescribePeer(slot, ++_sequence, true);

		var snapshots = new WorldSnapshot[frameCount];
		var payloads = new byte[frameCount][];
		for (int index = 0; index < frameCount; index++)
		{
			StepOnce();
			_sequence++;
			snapshots[index] = DescribePeer(0, _sequence, false);
			payloads[index] = MemoryPackSerializer.Serialize(snapshots[index]);
		}

		var serializeMicros = new double[frameCount];
		long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
		for (int index = 0; index < frameCount; index++)
		{
			long start = Stopwatch.GetTimestamp();
			payloads[index] = MemoryPackSerializer.Serialize(snapshots[index]);
			serializeMicros[index] = Elapsed(start);
		}

		double serializeAllocated = (GC.GetAllocatedBytesForCurrentThread() - allocationBefore) / (double)frameCount;
		Array.Sort(serializeMicros);

		var encodeMicros = new double[frameCount];
		allocationBefore = GC.GetAllocatedBytesForCurrentThread();
		for (int index = 0; index < frameCount; index++)
		{
			long start = Stopwatch.GetTimestamp();
			SnapshotWireFormat.EncodeDelta(payloads[index]);
			encodeMicros[index] = Elapsed(start);
		}

		double encodeAllocated = (GC.GetAllocatedBytesForCurrentThread() - allocationBefore) / (double)frameCount;
		Array.Sort(encodeMicros);

		int largestPayload = 0;
		double uncompressedBytes = 0;
		for (int index = 0; index < frameCount; index++)
		{
			largestPayload = Math.Max(largestPayload, payloads[index].Length);
			uncompressedBytes += payloads[index].Length + SnapshotWireFormat.FrameHeaderBytes;
		}

		long copyStart = Stopwatch.GetTimestamp();
		var copyBuffer = new byte[largestPayload];
		for (int index = 0; index < frameCount; index++) payloads[index].AsSpan().CopyTo(copyBuffer);
		double uncompressedMicros = Elapsed(copyStart) / frameCount;

		int compressionBound = Compressor.GetCompressBound(largestPayload);

		int[] levels = { 1, 2, 3, 5, 7, 9, 12, 15, 19 };
		var samples = new List<ZstdLevelSample>(levels.Length);

		foreach (int level in levels)
		{
			var compressor = new Compressor(level);
			var buffer = new byte[compressionBound];
			var frameMicros = new double[frameCount];
			long framedBytes = 0;

			for (int index = 0; index < frameCount; index++)
			{
				long start = Stopwatch.GetTimestamp();
				int compressedLength = compressor.Wrap(payloads[index], buffer);
				frameMicros[index] = Elapsed(start);
				framedBytes += compressedLength + SnapshotWireFormat.FrameHeaderBytes;
			}

			Array.Sort(frameMicros);
			double bytesPerFrame = framedBytes / (double)frameCount;

			// The median rather than the mean: the first frames of a fresh encoder carry its window setup, which a peer
			// pays once when it connects rather than on every frame of the stream.
			samples.Add(new ZstdLevelSample(
				level,
				bytesPerFrame,
				Percentile(frameMicros, 0.50),
				Percentile(frameMicros, 0.95),
				bytesPerFrame / (uncompressedBytes / frameCount)));
		}

		return new ZstdSweep(
			uncompressedBytes / frameCount,
			uncompressedMicros,
			frameCount,
			Percentile(serializeMicros, 0.50),
			serializeAllocated,
			Percentile(encodeMicros, 0.50),
			encodeAllocated,
			samples);
	}

	/// <summary>
	///     Frames the same delta's units three ways: as one record per unit, which is what a peer received before the
	///     section became columnar; the same values laid out field by field but still as floats, which is the layout
	///     change alone; and as the quantized columnar section a peer is sent now. Each goes through its own encoder at the
	///     level a delta uses, because a stream that alternated between layouts would let each find matches in the other.
	/// </summary>
	public UnitSectionComparison MeasureUnitSection(int frameCount)
	{
		for (int slot = 0; slot < _options.Players; slot++) DescribePeer(slot, ++_sequence, true);

		double recordSerialized = 0;
		double recordFrame = 0;
		double recordMicros = 0;
		double fieldMajorBytes = 0;
		double fieldMajorFrame = 0;
		double sectionSerialized = 0;
		double sectionFrame = 0;
		double sectionMicros = 0;
		double units = 0;

		var recordCompressor = new Compressor(SnapshotWireFormat.DeltaCompressionLevel);
		var fieldMajorCompressor = new Compressor(SnapshotWireFormat.DeltaCompressionLevel);
		var sectionCompressor = new Compressor(SnapshotWireFormat.DeltaCompressionLevel);
		var recordBuffer = new byte[Compressor.GetCompressBound(1 << 20)];
		var fieldMajorBuffer = new byte[Compressor.GetCompressBound(1 << 20)];
		var sectionBuffer = new byte[Compressor.GetCompressBound(1 << 20)];
		var fieldMajorWriter = new byte[1 << 20];
		var records = new List<UnitSnapshot>(256);

		for (int index = 0; index < frameCount; index++)
		{
			StepOnce();
			_sequence++;
			WorldSnapshot snapshot = DescribePeer(0, _sequence, false);
			units += snapshot.Units.Count;

			records.Clear();
			foreach (UnitSnapshot unit in snapshot.Units) records.Add(unit);
			byte[] serializedRecords = MemoryPackSerializer.Serialize(records);
			long start = Stopwatch.GetTimestamp();
			int recordLength = recordCompressor.Wrap(serializedRecords, recordBuffer);
			recordMicros += Elapsed(start);
			recordSerialized += serializedRecords.Length;
			recordFrame += recordLength + SnapshotWireFormat.FrameHeaderBytes;

			int fieldMajorLength = WriteColumnMajor(snapshot.Units, fieldMajorWriter);
			int fieldMajorCompressed = fieldMajorCompressor.Wrap(fieldMajorWriter.AsSpan(0, fieldMajorLength), fieldMajorBuffer);
			fieldMajorBytes += fieldMajorLength;
			fieldMajorFrame += fieldMajorCompressed + SnapshotWireFormat.FrameHeaderBytes;

			start = Stopwatch.GetTimestamp();
			byte[] serializedSection = MemoryPackSerializer.Serialize(snapshot.Units);
			int sectionLength = sectionCompressor.Wrap(serializedSection, sectionBuffer);
			sectionMicros += Elapsed(start);
			sectionSerialized += serializedSection.Length;
			sectionFrame += sectionLength + SnapshotWireFormat.FrameHeaderBytes;
		}

		return new UnitSectionComparison(
			units / frameCount,
			recordSerialized / frameCount,
			recordFrame / frameCount,
			recordMicros / frameCount,
			fieldMajorBytes / frameCount,
			fieldMajorFrame / frameCount,
			sectionSerialized / frameCount,
			sectionFrame / frameCount,
			sectionMicros / frameCount);
	}

	/// <summary>
	///     Writes one peer's units field by field into <paramref name="destination" /> and returns the bytes it used: the
	///     count, then every entity's id, then every owner, then each position axis, then the scalars, then the flags, and
	///     finally the ids of the units themselves. Every column carries the same value of the same kind, which is what an
	///     encoder can find redundancy in and what a vector copy could move.
	/// </summary>
	private static int WriteColumnMajor(IReadOnlyList<UnitSnapshot> units, byte[] destination)
	{
		int count = units.Count;
		int offset = 0;
		BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset), count);
		offset += 4;

		for (int index = 0; index < count; index++) offset = WriteInt32(destination, offset, units[index].EntityId);
		for (int index = 0; index < count; index++) offset = WriteInt32(destination, offset, units[index].OwnerPlayerEntityId);
		for (int index = 0; index < count; index++) offset = WriteInt32(destination, offset, units[index].OwnerPlayerIndex);

		for (int index = 0; index < count; index++) offset = WriteSingle(destination, offset, units[index].Position.X);
		for (int index = 0; index < count; index++) offset = WriteSingle(destination, offset, units[index].Position.Y);
		for (int index = 0; index < count; index++) offset = WriteSingle(destination, offset, units[index].Position.Z);
		for (int index = 0; index < count; index++) offset = WriteSingle(destination, offset, units[index].RotationY);
		for (int index = 0; index < count; index++) offset = WriteSingle(destination, offset, units[index].CurrentHp);
		for (int index = 0; index < count; index++) offset = WriteSingle(destination, offset, units[index].MaxHp);

		for (int index = 0; index < count; index++)
		{
			int flags = (units[index].IsDead ? 1 : 0) | (units[index].IsBuilding ? 2 : 0);
			destination[offset++] = (byte)flags;
		}

		for (int index = 0; index < count; index++)
		{
			string unitId = units[index].UnitId ?? string.Empty;
			destination[offset++] = (byte)unitId.Length;
			for (int character = 0; character < unitId.Length; character++) destination[offset++] = (byte)unitId[character];
		}

		return offset;
	}

	private static int WriteInt32(byte[] destination, int offset, int value)
	{
		BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset), value);
		return offset + 4;
	}

	private static int WriteSingle(byte[] destination, int offset, float value)
	{
		BinaryPrimitives.WriteSingleLittleEndian(destination.AsSpan(offset), value);
		return offset + 4;
	}

	/// <summary>
	///     What a delta frame carries, section by section, on the same peer. The frame is measured on a tick that
	///     follows a baseline, so every section holds what a match in progress actually sends rather than a catalog or a
	///     content payload that only a baseline carries.
	/// </summary>
	private DeltaComposition MeasureComposition()
	{
		DescribePeer(0, ++_sequence, true);
		StepOnce();
		_sequence++;

		WorldSnapshot snapshot = DescribePeer(0, _sequence, false);
		byte[] serialized = MemoryPackSerializer.Serialize(snapshot);
		double frameBytes = SnapshotWireFormat.EncodeDelta(serialized).Length;

		return new DeltaComposition(
			frameBytes,
			serialized.Length,
			MemoryPackSerializer.Serialize(snapshot.Units).Length,
			snapshot.Units.Count,
			MemoryPackSerializer.Serialize(snapshot.ComponentDeltas).Length,
			snapshot.ComponentDeltas.Count,
			MemoryPackSerializer.Serialize(snapshot.AbilityStates).Length,
			MemoryPackSerializer.Serialize(snapshot.Catalog).Length,
			MemoryPackSerializer.Serialize(snapshot.ColdPayloads).Length);
	}

	private double MeasureAllVisibleDelta()
	{
		_context.ShroudDisabled = true;

		for (int slot = 0; slot < _options.Players; slot++) DescribePeer(slot, ++_sequence, true);

		// Every unit has to have changed for this to measure what describing them costs: a delta that skipped the
		// units standing still would measure how empty a description can be, not how large a full-board one is.
		StepOnce();
		_sequence++;

		double bytes = 0;
		for (int slot = 0; slot < _options.Players; slot++)
		{
			WorldSnapshot snapshot = DescribePeer(slot, _sequence + 1, false);
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
