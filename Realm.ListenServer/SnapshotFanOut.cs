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
		for (int index = 0; index < _builders.Length; index++) _builders[index].PrepareFor(peerIdByPlayerSlot);
	}

	public AuthoritativeSnapshotBuilder Rent()
	{
		if (_idleBuilders.TryTake(out AuthoritativeSnapshotBuilder? builder, RentTimeoutMilliseconds)) return builder;

		throw new InvalidOperationException(
			$"all {_builders.Length} snapshot builders are still checked out after {RentTimeoutMilliseconds} ms; a description was rented and never returned");
	}

	private const int RentTimeoutMilliseconds = 5000;

	public void Return(AuthoritativeSnapshotBuilder builder) => _idleBuilders.Add(builder);

	public void ForgetPeer(int peerId)
	{
		_peerStates.Forget(peerId);
		Abilities?.ForgetPeer(peerId);
	}
}
