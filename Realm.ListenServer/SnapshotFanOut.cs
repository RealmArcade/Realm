using System;
using System.Collections.Concurrent;
using Realm.Ecs;
using Realm.Ecs.Services;

namespace Realm.ListenServer;

public class SnapshotFanOut
{
	public const int MaximumWorkerCount = 8;

	public const int DefaultWorkerLimit = 3;

	private readonly AuthoritativeSnapshotBuilder[] _builders;
	private readonly BlockingCollection<AuthoritativeSnapshotBuilder> _idleBuilders = new();
	private readonly SnapshotPeerStateStore _peerStates = new();
	private readonly SnapshotDescriptionCache _descriptions = new(AuthoritativeSnapshotBuilder.UnitCodecCount);
	private readonly object _abilityLock = new();
	private readonly int[] _peerIdByPlayerSlot = new int[EcsWorld.MAX_PLAYERS];

	private readonly EcsWorld _world;
	private readonly SimulationContext _context;
	private readonly Entity _worldEntity;
	private readonly MapRulesCatalog _rules;

	public SnapshotFanOut(EcsWorld world, SimulationContext context, Entity worldEntity, MapRulesCatalog rules, int workerCount)
	{
		_world = world;
		_context = context;
		_worldEntity = worldEntity;
		_rules = rules;

		WorkerCount = Math.Clamp(workerCount, 1, MaximumWorkerCount);
		_builders = new AuthoritativeSnapshotBuilder[WorkerCount];

		for (int index = 0; index < WorkerCount; index++)
		{
			var builder = CreateBuilder();
			_builders[index] = builder;
			_idleBuilders.Add(builder);
		}
	}

	/// <summary>
	///     Where a description that had to be built outside the pool says so. It is said rather than swallowed because the
	///     pool being empty is a defect in this class, and the match recovering from it should not hide that.
	/// </summary>
	public Action<string>? Report { get; set; }

	private AuthoritativeSnapshotBuilder CreateBuilder() => new(_world, _context, _worldEntity, _rules, _peerStates, _descriptions, _abilityLock);

	public int WorkerCount { get; }

	public static int DefaultWorkerCount => Math.Clamp(Environment.ProcessorCount - 1, 1, DefaultWorkerLimit);

	public ScriptAbilityRegistry? Abilities
	{
		get => _builders[0].Abilities;
		set
		{
			for (int index = 0; index < _builders.Length; index++) _builders[index].Abilities = value;
		}
	}

	public ServerTechLevels? TechLevels
	{
		get => _builders[0].TechLevels;
		set
		{
			for (int index = 0; index < _builders.Length; index++) _builders[index].TechLevels = value;
		}
	}

	public void PrepareFor(ReadOnlySpan<int> peerIdByPlayerSlot)
	{
		peerIdByPlayerSlot.CopyTo(_peerIdByPlayerSlot);
		for (int index = 0; index < _builders.Length; index++) _builders[index].PrepareFor(peerIdByPlayerSlot);
	}

	public AuthoritativeSnapshotBuilder Rent()
	{
		if (_idleBuilders.TryTake(out AuthoritativeSnapshotBuilder? builder, RentTimeoutMilliseconds)) return builder;

		// A builder checked out and never returned is a defect in this class, but it is not a reason for the match to stop
		// describing its players: one more is built for this description alone and dropped with it, so the publication that
		// found the pool empty still goes out.
		Report?.Invoke($"all {_builders.Length} snapshot builders are still checked out after {RentTimeoutMilliseconds} ms; a description is built outside the pool");
		AuthoritativeSnapshotBuilder fallback = CreateBuilder();
		fallback.PrepareFor(_peerIdByPlayerSlot);
		return fallback;
	}

	private const int RentTimeoutMilliseconds = 5000;

	public void Return(AuthoritativeSnapshotBuilder builder)
	{
		// A builder that was built because the pool was empty belongs to the caller that needed it, not to the pool.
		if (Array.IndexOf(_builders, builder) < 0) return;

		_idleBuilders.Add(builder);
	}

	public void EnterPeerDescription(int peerId) => _peerStates.Get(peerId).EnterDescription();

	public void ExitPeerDescription(int peerId) => _peerStates.Get(peerId).ExitDescription();

	public void ForgetPeer(int peerId)
	{
		_peerStates.Forget(peerId);
		Abilities?.ForgetPeer(peerId);
	}
}
