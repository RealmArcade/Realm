using System;
using System.Collections.Concurrent;
using Realm.Ecs;
using Realm.Ecs.Services;

namespace Realm.ListenServer;

/// <summary>
///     Describes peers across a fixed set of lanes. Each lane owns one builder for the whole match, so describing a peer
///     never waits for another lane to hand one back: the work is assigned to lanes up front and a lane only ever touches
///     its own builder and the peer slots it was given.
/// </summary>
public class SnapshotFanOut
{
	public const int MaximumWorkerCount = 8;

	public const int DefaultWorkerLimit = 3;

	private readonly AuthoritativeSnapshotBuilder[] _builders;
	private readonly SnapshotPeerStateStore _peerStates = new();
	private readonly SnapshotDescriptionCache _descriptions = new(AuthoritativeSnapshotBuilder.UnitCodecCount);
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
			_builders[index] = new AuthoritativeSnapshotBuilder(_world, _context, _worldEntity, _rules, _peerStates, _descriptions);
		}
	}

	/// <summary>
	///     Where a description that had to be built outside a lane says so. It is said rather than swallowed because a
	///     caller asking for a lane the match does not have is a defect in this class, and the match recovering from it
	///     should not hide that.
	/// </summary>
	public Action<string>? Report { get; set; }

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
		for (int index = 0; index < _peerIdByPlayerSlot.Length; index++)
		{
			if (_peerIdByPlayerSlot[index] != 0) _peerStates.Ensure(_peerIdByPlayerSlot[index]);
		}

		for (int index = 0; index < _builders.Length; index++) _builders[index].PrepareFor(peerIdByPlayerSlot);
	}

	public void PrepareWholeBoard(int sequence) => _builders[0].PrepareWholeBoard(sequence);

	public void WarmDescriptionQueries() => _builders[0].WarmDescriptionQueries();

	/// <summary>
	///     The builder belonging to a lane. Outside the pool's lanes a description is built on lane zero, which is the
	///     lane a single-threaded caller is always given.
	/// </summary>
	public AuthoritativeSnapshotBuilder GetLane(int laneIndex)
	{
		if ((uint)laneIndex < (uint)_builders.Length) return _builders[laneIndex];

		Report?.Invoke($"lane {laneIndex} does not exist among {_builders.Length} publish lanes; lane zero describes it instead");
		return _builders[0];
	}

	public void EnterPeerDescription(int peerId) => _peerStates.Get(peerId).EnterDescription();

	public void ExitPeerDescription(int peerId) => _peerStates.Get(peerId).ExitDescription();

	public void ForgetPeer(int peerId)
	{
		_peerStates.Forget(peerId);
		Abilities?.ForgetPeer(peerId);
	}
}
