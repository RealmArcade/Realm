using System.Collections.Concurrent;
using Realm.Ecs;
using Realm.Ecs.Services;

namespace Realm.ListenServer;

/// <summary>
///     Describes the world to several peers at once. One description is independent of every other — it reads the world
///     and the state of the single peer it is for — so the peers are spread across the cores the machine has, which is
///     where almost all of a match's per-tick cost lives. The builders are rented rather than shared: a builder holds
///     scratch that only one thread may use at a time, and what they do share is the per-peer state, which is only ever
///     written by the thread describing that peer.
/// </summary>
public class SnapshotFanOut
{
	/// <summary>How many descriptions may run at once, whatever a caller asks for.</summary>
	public const int MaximumWorkerCount = 8;

	/// <summary>
	///     How many descriptions this machine is given unless it is told otherwise. Measured on a 300-unit room of
	///     twenty-four players, four workers cut the describing phase from 17.6 ms to 9.4 ms and the next four cut it to
	///     8.9 ms: beyond four the phase is bound by the memory each description walks rather than by arithmetic, so the
	///     rest of the cores are left to the authority, the transport, and on this machine's own screen the client it is
	///     also running.
	/// </summary>
	public const int DefaultWorkerLimit = 4;

	private readonly AuthoritativeSnapshotBuilder[] _builders;
	private readonly ConcurrentBag<AuthoritativeSnapshotBuilder> _idleBuilders = new();
	private readonly SnapshotPeerStateStore _peerStates = new();
	private readonly SnapshotDescriptionCache _descriptions = new();
	private readonly object _abilityLock = new();

	public SnapshotFanOut(EcsWorld world, SimulationContext context, Entity worldEntity, MapRulesCatalog rules, int workerCount)
	{
		WorkerCount = Math.Clamp(workerCount, 1, MaximumWorkerCount);
		_builders = new AuthoritativeSnapshotBuilder[WorkerCount];

		for (int index = 0; index < WorkerCount; index++)
		{
			var builder = new AuthoritativeSnapshotBuilder(world, context, worldEntity, rules, _peerStates, _descriptions, _abilityLock);
			_builders[index] = builder;
			_idleBuilders.Add(builder);
		}
	}

	/// <summary>How many peers this fan-out may be describing at the same time.</summary>
	public int WorkerCount { get; }

	/// <summary>The worker count this machine is given unless a match asks for another one.</summary>
	public static int DefaultWorkerCount => Math.Clamp(Environment.ProcessorCount - 1, 1, DefaultWorkerLimit);

	/// <summary>
	///     What the map registered about abilities. A map marks an order unavailable or hides a button on the authority,
	///     so how each unit's abilities are shown is described from here rather than worked out again on every client.
	/// </summary>
	public ScriptAbilityRegistry? Abilities
	{
		get => _builders[0].Abilities;
		set
		{
			for (int index = 0; index < _builders.Length; index++) _builders[index].Abilities = value;
		}
	}

	/// <summary>How far each player is up each of the map's tech tracks, which a peer that joins a match in progress learns.</summary>
	public ServerTechLevels? TechLevels
	{
		get => _builders[0].TechLevels;
		set
		{
			for (int index = 0; index < _builders.Length; index++) _builders[index].TechLevels = value;
		}
	}

	/// <summary>Tells every builder which peer acts as each player slot. Only seats that exist are named; the rest stay zero.</summary>
	public void PrepareFor(ReadOnlySpan<int> peerIdByPlayerSlot)
	{
		for (int index = 0; index < _builders.Length; index++) _builders[index].PrepareFor(peerIdByPlayerSlot);
	}

	/// <summary>
	///     Takes a builder for one description. A caller that asks for more at once than this fan-out has waits for one to
	///     come back, which is why the number of concurrent descriptions is bounded by <see cref="WorkerCount" />.
	/// </summary>
	public AuthoritativeSnapshotBuilder Rent()
	{
		while (true)
		{
			if (_idleBuilders.TryTake(out AuthoritativeSnapshotBuilder? builder)) return builder;

			Thread.SpinWait(64);
		}
	}

	/// <summary>Gives a builder back once its description has been serialized.</summary>
	public void Return(AuthoritativeSnapshotBuilder builder) => _idleBuilders.Add(builder);

	/// <summary>Forgets a peer that has gone, so the connection taking its place is described from scratch.</summary>
	public void ForgetPeer(int peerId)
	{
		_peerStates.Forget(peerId);
		Abilities?.ForgetPeer(peerId);
	}
}
