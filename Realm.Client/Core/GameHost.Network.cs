using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using Realm.Ecs;
using Godot;
using MemoryPack;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.MapAPI;

namespace Realm.Client.Core;

public partial class GameHost
{
	private int _pendingSelectUnitServerId = -1;

	private int GetServerEntityId(Entity localEntity) =>
		GameContext.NetworkService.GetServerEntityId(localEntity.Id);

	public bool TryGetLocalEntity(int serverEntityId, out Entity localEntity)
	{
		if (GameContext.NetworkService == null)
		{
			localEntity = Entity.Null;
			return false;
		}

		return GameContext.NetworkService.TryGetLocalEntity(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone) =>
		GameContext.NetworkService.SetBackupResources(gold, wood, stone);

	public void KillUnitDeferredExternal(Unit3D unit)
	{
		Callable.From(() => KillUnitDeferred(unit)).CallDeferred();
	}

	private void RetireUnitFromView(Unit3D unit)
	{
		if (!GodotObject.IsInstanceValid(unit) || !AllUnits.Contains(unit)) return;

		RemoveUnitFromCollections(unit);
		GameContext.SimulationService.DestroyUnitEntityDomain(unit.Entity);
		unit.QueueFree();
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private void SubmitOrderCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, targetEntityId, argString);
		RegisterPredictedCommand(commandId, unitIds, commandType, targetNumerics);

		if (HasRemoteAuthority)
		{
			RpcId(1, nameof(SubmitCommand), payload);
			return;
		}

		GameContext.LocalCommandTransport.Submit(ResolveLocalCommandSenderPeerId(), payload);
	}

	/// <summary>
	/// Submits commands without registering them for prediction. The harness uses this to exercise the host's command
	/// budget: a flood must be refused by the authority, and registering hundreds of predicted orders locally would
	/// measure the client's rollback bookkeeping instead of the host's rate limit.
	/// </summary>
	public void SubmitCommandFloodForHarness(string commandType, List<int> unitIds, Vector3 targetPos, int count)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		for (int index = 0; index < count; index++)
		{
			var (_, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, 0, string.Empty);
			RpcId(1, nameof(SubmitCommand), payload);
		}
	}

	private void SubmitRemoteCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		var (commandId, payload) = GameContext.NetworkService.BuildClientCommand(commandType, unitIds, targetNumerics, targetEntityId, argString);
		RegisterPredictedCommand(commandId, unitIds, commandType, targetNumerics);
		RpcId(1, nameof(SubmitCommand), payload);
	}

	private int ResolveLocalCommandSenderPeerId()
	{
		if (_multiplayerActive) return _localPeerId;

		int localPlayerIndex = ObservingPlayerIndex;
		if (localPlayerIndex > 0)
		{
			Entity localPlayerEntity = GetPlayerEntityForPlayerIndex(localPlayerIndex);
			if (localPlayerEntity != Entity.Null)
			{
				foreach (var pair in _peerIdToPlayerEntityMap)
				{
					if (pair.Key >= 0 && pair.Value == localPlayerEntity) return pair.Key;
				}
			}
		}

		return 1;
	}

	private void DrainLocalCommandQueue()
	{
		var transport = GameContext.LocalCommandTransport;
		while (transport.TryDequeue(out PendingLocalCommand pending))
		{
			NetworkCommand command = MemoryPackSerializer.Deserialize<NetworkCommand>(pending.Payload);
			if (IsDuplicateCommand(pending.SenderPeerId, command.CommandId)) continue;
			ApplyAuthoritativeCommand(pending.SenderPeerId, command);
		}
	}

	private void RegisterPredictedCommand(int commandId, List<int> serverEntityIds, string commandType, System.Numerics.Vector3 targetPosition)
	{
		if (!GameSettings.MovementPrediction || GameContext.MovementPredictionService == null) return;
		if (GameContext.World == null) return;

		int localPlayerIndex = LocalPlayerIndex;
		if (localPlayerIndex < 0) return;

		_predictedCommandEntities.Clear();
		for (int index = 0; index < serverEntityIds.Count; index++)
		{
			if (!TryResolveLocalEntity(serverEntityIds[index], out Entity localEntity)) continue;
			if (!GameContext.World.IsAlive(localEntity)) continue;
			if (!GameContext.World.Has<UnitOwnerPlayer>(localEntity)) continue;
			if (GameContext.World.Get<UnitOwnerPlayer>(localEntity).PlayerIndex != localPlayerIndex) continue;
			_predictedCommandEntities.Add(localEntity.Id);
		}

		if (_predictedCommandEntities.Count == 0) return;
		GameContext.MovementPredictionService.RegisterPredictedCommand(commandId, _predictedCommandEntities);
		GameContext.MovementPredictionService.ApplyPredictedOrder(
			GameContext.World,
			ResolvePredictedOrderKind(commandType),
			System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_predictedCommandEntities),
			targetPosition,
			commandType.EndsWith("_queued", StringComparison.Ordinal));
	}

	private bool TryResolveLocalEntity(int serverEntityId, out Entity localEntity)
	{
		if (TryGetLocalEntity(serverEntityId, out localEntity)) return true;
		if (!HasAuthority)
		{
			localEntity = Entity.Null;
			return false;
		}

		localEntity = new Entity(serverEntityId);
		return GameContext.World != null && GameContext.World.IsAlive(localEntity);
	}

	private static PredictedOrderKind ResolvePredictedOrderKind(string commandType)
	{
		return commandType switch
		{
			"move" => PredictedOrderKind.Move,
			"attack_move" => PredictedOrderKind.AttackMove,
			"attack_move_queued" => PredictedOrderKind.AttackMove,
			"gather" => PredictedOrderKind.Gather,
			"build" => PredictedOrderKind.Build,
			"stop" => PredictedOrderKind.Stop,
			_ => PredictedOrderKind.None
		};
	}

	private readonly List<int> _predictedCommandEntities = new();

	public int GetLocalPlayerIndexForHarness() => LocalPlayerIndex;

	public int GetServerEntityIdForHarness(int localEntityId) => GameContext.NetworkService.GetServerEntityId(localEntityId);

	public void IssueRawCommandForHarness(string commandType, List<int> serverEntityIds, Vector3 targetPosition, int targetEntityId = 0, string argString = "")
	{
		SubmitOrderCommand(commandType, serverEntityIds, targetPosition, targetEntityId, argString);
	}

	private void UpdateClientCameraPosition()
	{
		var camera = GetViewport().GetCamera3D();
		if (camera == null) return;
		var pos = new NetworkVector3(camera.GlobalPosition);
		RpcId(1, nameof(UpdateClientCamera), MemoryPackSerializer.Serialize(pos));
	}

	private void UpdateClientWorldState(float fDelta)
	{
		bool isEditorMode = GameContext.SessionState.IsMapEditorMode;
		bool advanceSimulationClock = !_multiplayerActive || Multiplayer.IsServer();
		var res = GameContext.EnvironmentService.UpdateClientWorldStateDomain(fDelta, isEditorMode, advanceSimulationClock);
		if (res.UpdateVisuals)
		{
			UpdateClientWorldStateViewLogic(res.Progress);
		}
	}

	private void UpdateClientWorldStateViewLogic(float progress)
	{
		UpdateDayNightVisuals(progress);
	}

	private void UpdateClientNavMesh()
	{
		// A peer refreshes the mesh only for the obstacle set it had to receive after its first bake - the map's props and
		// pre-placed buildings. A building that appears mid-match is carved on both sides (HandleBuildingSpawn), and baking
		// it into the geometry on one side only is what left the two meshes with a different polygon split.
		UpdateNavMeshRefresh();
	}

	private void UpdateClientTick(float fDelta)
	{
		CapturePreviousStateSnapshots();

		UpdateClientNavMesh();

		_commandSendTimer += fDelta;
		if (_commandSendTimer >= 0.05f)
		{
			_commandSendTimer = 0f;
			UpdateClientTickViewLogic();
		}

		GameContext.NetworkService.UpdateClientTickDomain();
		GameContext.NetworkLatencyService.Update(fDelta);

		GameContext.MovementPredictionService?.Advance(GameContext.World, fDelta);

		UpdateClientWorldState(fDelta);

		var query = QueryCache.AllInterpolationTargetQuery;
		GameContext.SimulationService.SetDelta(fDelta);
		GameContext.World.Query(in query, GameContext.SimulationService.InterpolationQueryDelegate);
		UpdateVisualNodesFromEcs(fDelta);
		UpdateBuildingPreview();
	}

	private void UpdateClientTickViewLogic()
	{
		UpdateClientCameraPosition();
	}

	private void StopUnitVelocities(IEnumerable<int> entityIds)
	{
		foreach (int entityId in entityIds)
		{
			foreach (var unit in AllUnits)
			{
				if (unit.Entity.Id == entityId)
				{
					unit.Velocity = Vector3.Zero;
					break;
				}
			}
		}
	}

	private void ProcessServerCommandBuild(ServerCommandResult result)
	{
		Entity buildWorkerEntity = Entity.Null;
		if (result.BuildWorkerServerEntityId >= 0)
		{
			buildWorkerEntity = GameContext.NetworkService.FindServerEntity(result.BuildWorkerServerEntityId, AllUnits);
			if (buildWorkerEntity == Entity.Null || !GameContext.World.IsAlive(buildWorkerEntity))
			{
				GD.PushWarning($"[Build] Rejected '{result.BuildUnitType}': worker server entity {result.BuildWorkerServerEntityId} is not a live unit on the host.");
				return;
			}
		}

		UnitMetadata? buildMetadata = GameContext.RegistryService.FindUnitOrBuildingMetadata(result.BuildUnitType);
		_peerIdToPlayerEntityMap.TryGetValue(result.BuildPeerOwner, out Entity buildPeerOwnerEntity);
		var domainRes = GameContext.NetworkService.ProcessServerCommandBuildDomain(result, buildMetadata, buildPeerOwnerEntity, buildWorkerEntity);
		if (!domainRes.Success)
		{
			GD.PushWarning($"[Build] Rejected '{result.BuildUnitType}': build metadata is missing from the unit registry or the worker is not owned by the commanding peer.");
			return;
		}

		var godotPos = new Vector3(domainRes.BuildPosition.X, domainRes.BuildPosition.Y, domainRes.BuildPosition.Z);
		if (buildWorkerEntity != Entity.Null)
		{
			AssignBuildTaskToWorker(buildWorkerEntity, domainRes.BuildUnitType, domainRes.BuildPosition);
			GD.Print($"[Build] Worker {buildWorkerEntity.Id} started constructing '{domainRes.BuildUnitType}' at {domainRes.BuildPosition}.");
			return;
		}

		var bldEntity = CreateEcsUnit(domainRes.BuildUnitType, domainRes.Name, domainRes.MaxHp, domainRes.Damage, domainRes.Range, domainRes.Armor, 0f, godotPos, domainRes.PlayerOwner);
		ProcessServerCommandBuildViewLogic(bldEntity, domainRes.BuildUnitType, domainRes.TargetModel, godotPos, domainRes.PlayerOwner);
	}

	private void ProcessServerCommandBuildViewLogic(Entity bldEntity, string buildUnitType, string targetModel, Vector3 buildPosition, PlayerEntity playerOwner)
	{
		string modelPath = GetFallbackModelPath(targetModel, true);
		int playerIndex = (playerOwner.Value != Entity.Null && GameContext.World.Has<UnitOwnerPlayer>(playerOwner.Value))
			? GameContext.World.Get<UnitOwnerPlayer>(playerOwner.Value).PlayerIndex
			: 0;
		SpawnUnit3D(bldEntity, buildUnitType, modelPath, buildPosition, true, playerIndex);
	}

	private UnitId GetSpellCaster(NetworkCommand cmd)
	{
		var casterEntity = GameContext.NetworkService.GetSpellCasterEntityDomain(cmd, AllUnits);
		return casterEntity != Entity.Null ? (UnitId)casterEntity.Id : UnitId.Invalid;
	}

	private void ApplySpellEffect(string spellId, Vector3 position, Entity casterEnt)
	{
		var pos = new System.Numerics.Vector3(position.X, position.Y, position.Z);
		GameContext.SimulationService.ApplySpellEffectDomain(spellId, pos, casterEnt);
	}

	private void BroadcastSpellEffect(string spellId, Vector3 position)
	{
		var peers = GameContext.NetworkService.GetPeersToBroadcastSpellEffect(_localPeerId);
		foreach (var peerId in peers)
		{
			QueueOrSendSpellEffect(peerId, spellId, position);
		}
	}

	private readonly Dictionary<int, int> _lastAppliedCommandIdPerPeer = new();

	private bool IsDuplicateCommand(int peerId, int commandId)
	{
		if (commandId <= 0) return true;

		if (_lastAppliedCommandIdPerPeer.TryGetValue(peerId, out int lastAppliedCommandId) && commandId <= lastAppliedCommandId) return true;

		_lastAppliedCommandIdPerPeer[peerId] = commandId;
		return false;
	}

	public void ResetCommandDedupe()
	{
		_lastAppliedCommandIdPerPeer.Clear();
	}

	private void ProcessServerCommandSpell(NetworkCommand cmd, ServerCommandResult result)
	{
		if (!result.NeedsSpellEffect) return;

		string spellId = result.SpellId;
		Vector3 position = result.SpellPosition;
		var caster = GetSpellCaster(cmd);
		var casterEnt = GameContext.SimulationService.TryGetEntity(caster, out var ce) ? ce : Entity.Null;

		if (ExecuteConsumablePurchase(caster, spellId, position)) return;

		OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));

		ApplySpellEffect(spellId, position, casterEnt);
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
		BroadcastSpellEffect(spellId, position);
	}

	private const int CameraPayloadBytes = 64;

	private static bool TryDeserializePeerPayload<T>(byte[]? payload, int maximumBytes, out T value) where T : struct
	{
		return PeerPayloadGuard.TryDeserialize(payload, maximumBytes, out value);
	}

	/// <summary>
	/// A peer may submit at most this many commands per second on average, with a burst allowance for the several orders a
	/// player issues while dragging a selection around. The budget exists because every accepted command runs the full
	/// authority path on the host tick: a peer that submits thousands per second would otherwise spend the simulation
	/// budget of everyone in the match. Legitimate play never approaches it, and going over it is a refusal the client is
	/// told about, not a silent drop.
	/// </summary>
	public const int CommandBudgetCapacity = 40;
	public const double CommandBudgetRefillPerSecond = 20.0;

	private readonly Dictionary<int, TokenBucket> _commandBudgets = new();
	private int _rateLimitedCommandCount;

	public int RateLimitedCommandCountForHarness => _rateLimitedCommandCount;

	/// <summary>
	/// The highest command id the rate limiter has refused for each peer since that peer was last told. Telling a peer
	/// about a refusal costs one reliable RPC back to it, so answering every refused command turns one inbound message
	/// into one outbound message: a peer that resent 400 commands every 50 ms on the reliable channel earned 295 315
	/// refusals in a single match, and the replies starved the snapshots and snapshot acknowledgements the peer needed to
	/// stay connected, for the whole 36 s the resend storm lasted. One notice per peer per quarter second, naming the
	/// highest id refused in that window, is enough for a client to revert the order it sent.
	/// </summary>
	private readonly Dictionary<int, int> _pendingRefusedCommandHighWater = new();
	private ulong _lastRefusedCommandNoticeMsec;
	private const ulong RefusedCommandNoticeIntervalMsec = 250;

	private void RecordRefusedCommand(int peerId, int commandId)
	{
		if (commandId <= 0) return;
		if (_pendingRefusedCommandHighWater.TryGetValue(peerId, out int pending) && pending >= commandId) return;
		_pendingRefusedCommandHighWater[peerId] = commandId;
	}

	private void FlushRefusedCommandNotices()
	{
		if (_pendingRefusedCommandHighWater.Count == 0) return;

		ulong now = Time.GetTicksMsec();
		if (now - _lastRefusedCommandNoticeMsec < RefusedCommandNoticeIntervalMsec) return;

		_lastRefusedCommandNoticeMsec = now;
		foreach (KeyValuePair<int, int> entry in _pendingRefusedCommandHighWater)
		{
			RpcId(entry.Key, nameof(AcknowledgeCommand), entry.Value, Array.Empty<int>(), Tr("Command rate limit exceeded."));
		}

		_pendingRefusedCommandHighWater.Clear();
	}

	private bool TryConsumeCommandBudget(int peerId)
	{
		if (!_commandBudgets.TryGetValue(peerId, out TokenBucket? budget))
		{
			budget = new TokenBucket(CommandBudgetCapacity, CommandBudgetRefillPerSecond);
			_commandBudgets[peerId] = budget;
		}

		if (budget.TryConsume(1)) return true;
		_rateLimitedCommandCount++;
		return false;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SubmitCommand(byte[] payload)
	{
		if (!Multiplayer.IsServer()) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		if (!TryDeserializePeerPayload(payload, NetworkCommandLimits.MaximumPayloadBytes, out NetworkCommand cmd))
		{
			GD.PushWarning($"[Network] Dropped a malformed command payload ({payload?.Length ?? 0} bytes) from peer {senderId}.");
			return;
		}

		if (!TryConsumeCommandBudget(senderId))
		{
			if (_rateLimitedCommandCount % 200 == 1) GD.PushWarning($"[Network] Peer {senderId} is above the command rate limit; {_rateLimitedCommandCount} commands refused so far.");
			RecordRefusedCommand(senderId, cmd.CommandId);
			return;
		}

		if (!NetworkCommandLimits.TrySanitize(ref cmd, out string sanitizeRejection))
		{
			GD.PushWarning($"[Network] Rejected a command from peer {senderId}: {sanitizeRejection}");
			if (cmd.CommandId > 0) RpcId(senderId, nameof(AcknowledgeCommand), cmd.CommandId, Array.Empty<int>(), sanitizeRejection);
			return;
		}

		var sw = Stopwatch.StartNew();
		if (IsDuplicateCommand(senderId, cmd.CommandId))
		{
			RpcId(senderId, nameof(AcknowledgeCommand), cmd.CommandId, Array.Empty<int>(), string.Empty);
			return;
		}

		ApplyAuthoritativeCommand(senderId, cmd);
		sw.Stop();
		float responseCpuMs = (float)sw.Elapsed.TotalMilliseconds;
		float adjustedResponseMs = responseCpuMs + GameContext.PerformanceTrackingService.TrackerLastTickDelay;
		if (GameContext.PerformanceTrackingService.TrackerApiDurations != null)
		{
			GameContext.PerformanceTrackingService.TrackerApiDurations.Add(adjustedResponseMs);
		}
	}

	private void ApplyAuthoritativeCommand(int senderId, NetworkCommand cmd)
	{
		var result = GameContext.NetworkService.ExecuteServerCommand(senderId, cmd, AllUnits, AllProps);

		StopUnitVelocities(result.StopVelocityEntityIds);
		StopUnitVelocities(result.HoldVelocityEntityIds);

		ProcessServerCommandBuild(result);
		ProcessServerCommandSpell(cmd, result);

		if (result.RejectedServerEntityIds.Count > 0)
		{
			PredictionDiagnostics.RecordRejection(cmd.CommandId, result.RejectionReason);
		}

		int[] rejectedEntityIds = result.RejectedServerEntityIds.ToArray();
		if (senderId == _localPeerId) DeliverLocalCommandAck(cmd.CommandId, rejectedEntityIds, result.RejectionReason);
		else RpcId(senderId, nameof(AcknowledgeCommand), cmd.CommandId, rejectedEntityIds, result.RejectionReason);
	}

	private void DeliverLocalCommandAck(int commandId, int[] rejectedServerEntityIds, string rejectionReason)
	{
		var prediction = GameContext.MovementPredictionService;
		if (prediction == null) return;

		if (!string.IsNullOrEmpty(rejectionReason))
		{
			PredictionDiagnostics.RecordRejection(commandId, rejectionReason);
			prediction.RollbackRejectedCommand(GameContext.World, commandId, rejectedServerEntityIds ?? Array.Empty<int>(), rejectionReason);
			return;
		}

		prediction.OnCommandAccepted(commandId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void AcknowledgeCommand(int commandId, int[] rejectedServerEntityIds, string rejectionReason)
	{
		DeliverLocalCommandAck(commandId, rejectedServerEntityIds, rejectionReason);
	}

	public void SendLatencyProbe(int sequence)
	{
		if (!HasRemoteAuthority) return;
		RpcId(1, nameof(SubmitLatencyProbe), sequence);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void SubmitLatencyProbe(int sequence)
	{
		if (!Multiplayer.IsServer()) return;
		RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReceiveLatencyProbe), sequence);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveLatencyProbe(int sequence)
	{
		GameContext.NetworkLatencyService.OnResponse(sequence);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void UpdateClientCamera(byte[] payload)
	{
		if (!Multiplayer.IsServer()) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		if (!TryDeserializePeerPayload(payload, CameraPayloadBytes, out NetworkVector3 pos)) return;
		GameContext.NetworkService.RecordClientCameraPosition(senderId, pos.ToGodot());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ReceiveSnapshot(byte[] payload)
	{
		ApplyReceivedSnapshot(payload);
	}

	public void SendSnapshotAck(int sequence, ulong appliedMask)
	{
		if (!_multiplayerActive || Multiplayer.IsServer() || Multiplayer.MultiplayerPeer == null) return;
		RpcId(1, nameof(AcknowledgeSnapshot), sequence, (long)appliedMask);
	}

	/// <summary>
	/// The peer's acknowledgement of what it applied. It travels on its own channel because the reliable channel is
	/// ordered: a 139 KB baseline being retransmitted over a lossy link used to hold every acknowledgement behind it,
	/// which made the host believe the peer had fallen far behind and answer with another full baseline. That loop was
	/// self-sustaining, and separating the channels is what breaks it.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferChannel = SnapshotAckChannel, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void AcknowledgeSnapshot(int sequence, long appliedMask)
	{
		if (!Multiplayer.IsServer()) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		if (senderId <= 1) return;
		GameContext.NetworkService.RecordClientSnapshotAck(senderId, sequence, (ulong)appliedMask);
	}

	public void RequestSnapshotBaseline()
	{
		if (!_multiplayerActive || Multiplayer.IsServer() || Multiplayer.MultiplayerPeer == null) return;
		RpcId(1, nameof(HandleSnapshotBaselineRequest));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void HandleSnapshotBaselineRequest()
	{
		if (!Multiplayer.IsServer()) return;
		int senderId = Multiplayer.GetRemoteSenderId();
		if (senderId <= 1) return;
		GameContext.NetworkService.HandleClientBaselineRequest(senderId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferChannel = BaselineChannel, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ReceiveBaselineSnapshot(byte[] payload)
	{
		ApplyReceivedSnapshot(payload);
	}

	private void ApplyReceivedSnapshot(byte[] payload)
	{
		if (Multiplayer.IsServer()) return;
		GameContext.NetworkService.RecordSnapshotReceived(payload);
		FlushReplicatedSnapshotEvents();
		MultiplayerSyncDiagnostics.RecordClientPresentation();
	}

	/// <summary>
	/// The most bytes one unreliable snapshot packet may carry. A delta above the MTU is fragmented by ENet, and losing
	/// any one fragment loses the whole snapshot: on the relayed profiles a single delta reached 2 444 bytes and 7-22 %
	/// of host sequences never arrived. A larger delta is split into independently addressed fragments and reassembled
	/// on the peer, so losing one costs one snapshot instead of all of them.
	/// </summary>
	public const int MaximumUnfragmentedSnapshotBytes = 1024;

	/// <summary>
	/// How many fragments one delta may be split into. A delta that needs more than this is a sign the payload belongs on
	/// the baseline path, and it is dropped rather than turned into an unbounded burst of packets.
	/// </summary>
	private const int MaximumSnapshotFragmentCount = 64;

	private int _snapshotFragmentGroupId;
	private int _newestSnapshotFragmentGroupId;
	private readonly Dictionary<int, SnapshotReassembly> _snapshotReassembly = new();

	private class SnapshotReassembly
	{
		private readonly byte[] _buffer;
		private readonly bool[] _received;
		private int _receivedCount;
		private int _writtenBytes;

		public readonly int FragmentCount;

		public SnapshotReassembly(int fragmentCount)
		{
			FragmentCount = fragmentCount;
			_buffer = new byte[fragmentCount * MaximumUnfragmentedSnapshotBytes];
			_received = new bool[fragmentCount];
		}

		public bool Claim(int fragmentIndex, byte[] fragment, int length)
		{
			if (_received[fragmentIndex]) return false;

			_received[fragmentIndex] = true;
			_receivedCount++;
			Buffer.BlockCopy(fragment, 0, _buffer, fragmentIndex * MaximumUnfragmentedSnapshotBytes, length);
			_writtenBytes += length;
			return true;
		}

		public bool IsComplete => _receivedCount == FragmentCount;

		public byte[] BuildPayload()
		{
			var payload = new byte[_writtenBytes];
			Buffer.BlockCopy(_buffer, 0, payload, 0, _writtenBytes);
			return payload;
		}
	}

	private void SendSnapshotPayload(int peerId, byte[] payload)
	{
		if (payload == null || payload.Length == 0) return;
		if (payload.Length <= MaximumUnfragmentedSnapshotBytes)
		{
			RpcId(peerId, nameof(ReceiveSnapshot), payload);
			return;
		}

		int fragmentCount = (payload.Length + MaximumUnfragmentedSnapshotBytes - 1) / MaximumUnfragmentedSnapshotBytes;
		if (fragmentCount > MaximumSnapshotFragmentCount)
		{
			GD.PushWarning($"[Network] A {payload.Length} byte snapshot needs {fragmentCount} fragments; it was not sent.");
			return;
		}

		_snapshotFragmentGroupId++;
		int groupId = _snapshotFragmentGroupId;
		for (int fragmentIndex = 0; fragmentIndex < fragmentCount; fragmentIndex++)
		{
			int offset = fragmentIndex * MaximumUnfragmentedSnapshotBytes;
			int length = Math.Min(MaximumUnfragmentedSnapshotBytes, payload.Length - offset);
			var fragment = new byte[length];
			Buffer.BlockCopy(payload, offset, fragment, 0, length);
			RpcId(peerId, nameof(ReceiveSnapshotFragment), groupId, fragmentCount, fragmentIndex, length, fragment);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveSnapshotFragment(int groupId, int fragmentCount, int fragmentIndex, int length, byte[] fragment)
	{
		if (Multiplayer.IsServer()) return;
		if (fragmentCount <= 0 || fragmentCount > MaximumSnapshotFragmentCount) return;
		if (fragmentIndex < 0 || fragmentIndex >= fragmentCount) return;
		if (length <= 0 || length > MaximumUnfragmentedSnapshotBytes) return;
		if (fragment == null || fragment.Length != length) return;
		if (groupId < _newestSnapshotFragmentGroupId) return;

		if (!_snapshotReassembly.TryGetValue(groupId, out SnapshotReassembly? reassembly))
		{
			reassembly = new SnapshotReassembly(fragmentCount);
			_snapshotReassembly[groupId] = reassembly;
		}

		if (reassembly.FragmentCount != fragmentCount) return;
		if (!reassembly.Claim(fragmentIndex, fragment, length)) return;
		if (!reassembly.IsComplete) return;

		_snapshotReassembly.Remove(groupId);
		if (groupId > _newestSnapshotFragmentGroupId)
		{
			_newestSnapshotFragmentGroupId = groupId;
			DropSupersededSnapshotReassemblies(groupId);
		}

		ApplyReceivedSnapshot(reassembly.BuildPayload());
	}

	private void DropSupersededSnapshotReassemblies(int newestGroupId)
	{
		List<int>? superseded = null;
		foreach (int groupId in _snapshotReassembly.Keys)
		{
			if (groupId >= newestGroupId - 1) continue;
			(superseded ??= new List<int>()).Add(groupId);
		}

		if (superseded == null) return;
		for (int index = 0; index < superseded.Count; index++) _snapshotReassembly.Remove(superseded[index]);
	}

	/// <summary>
	/// The ENet channel a peer's snapshot acknowledgement travels on. Separate from the channel that carries baselines
	/// and from the unreliable channel that carries deltas, so neither can delay it.
	/// </summary>
	public const int SnapshotAckChannel = 1;

	/// <summary>
	/// The ENet channel full baselines travel on. A baseline is the whole replicated world on its own channel so that
	/// retransmitting it cannot block every other reliable message to that peer.
	/// </summary>
	public const int BaselineChannel = 2;

	public void FlushReplicatedSnapshotEvents()
	{
		var pendingSpawns = GameContext.NetworkService.FlushPendingUnitSpawns();
		for (int i = 0; i < pendingSpawns.Count; i++)
		{
			SpawnUnitFromSnapshot(pendingSpawns[i]);
		}

		foreach (var localEntity in GameContext.NetworkService.FlushPendingUnitKills())
		{
			if (TryGetUnit3D(localEntity, out var unit3D))
			{
				Callable.From(() => KillUnitDeferred(unit3D)).CallDeferred();
			}
		}

		foreach (var localEntity in GameContext.NetworkService.FlushPendingUnitDespawns())
		{
			if (TryGetUnit3D(localEntity, out var unit3D))
			{
				Callable.From(() => RetireUnitFromView(unit3D)).CallDeferred();
			}
		}

		if (pendingSpawns.Count > 0)
		{
			GameContext.NetworkService.ReapplyAbilityStatesToUnits();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ReceiveNavMeshObstacleVolumes(Vector3[] positions, float[] radii)
	{
		if (Multiplayer.IsServer()) return;
		ApplyAuthoritativeNavMeshObstacles(positions, radii);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void PlaySpellEffect(string spellId, Vector3 position)
	{
		var def = GetAbilityDefinition(spellId);
		if (def != null)
		{
			GameContext.FXService.SpawnAbilityEffect(this, def, position);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSpawnArrowProjectile(Vector3 start, Vector3 target, int ownerPlayerIndex = 0)
	{
		GameContext.FXService.SpawnArrowProjectile(this, start, target, ownerPlayerIndex);
	}

	public void RefreshReplicatedOverlayUi()
	{
		GameContext.InGameHud?.InvalidateReplicatedOverlays();
	}

	public void SetReplicatedEntityVisible(Entity entity, bool visible)
	{
		if (!TryGetUnit3D(entity, out var unit) || !GodotObject.IsInstanceValid(unit)) return;
		unit.Visible = visible;
	}

	private int _shadowVerificationTickCounter;

	private void RunShadowClientVerification()
	{
		_shadowVerificationTickCounter++;
		if (_shadowVerificationTickCounter % 60 != 0) return;

		byte[] payload = GameContext.NetworkService.BuildExplicitBaselineSnapshot(1, AllUnits);
		if (payload.Length == 0) return;
		ShadowClientVerifier.VerifyPayload(1, payload, true);
	}

	private void UpdateServerSnapshotTick(float fDelta)
	{
		// Refusal notices leave on the snapshot tick rather than per refused command, so a peer that floods the authority
		// cannot make it answer once per message.
		FlushRefusedCommandNotices();
		ProcessDelayedSpectatorPackets();
		var snapshots = GameContext.NetworkService.BuildServerSnapshots(_localPeerId, AllUnits);
		if (PredictionDiagnostics.IsHostRecording)
		{
			Entity worldEntity = ECSContext.WorldEntity;
			int sequence = GameContext.World != null && GameContext.World.IsAlive(worldEntity) && GameContext.World.Has<NetworkState>(worldEntity)
				? GameContext.World.Get<NetworkState>(worldEntity).SnapshotSequence
				: -1;
			if (sequence >= 0) PredictionDiagnostics.RecordHostSnapshot(sequence, GameContext.World!, AllUnits);
		}
		foreach (var (peerId, payload, isBaseline) in snapshots)
		{
			ShadowClientVerifier.VerifyPayload(peerId, payload, isBaseline);
			// The volumes go out before the baseline that gives the peer its first units, so it never plans a route on a mesh
			// that is still missing the authority's obstacles. Both travel on the ordered reliable channel.
			if (isBaseline) SendNavMeshObstacleVolumesToPeer(peerId);
			QueueOrSendSnapshot(peerId, payload, isBaseline);
		}
	}

	private void SpawnUnitFromSnapshot(UnitSnapshot snap)
	{
		var entity = GameContext.NetworkService.SpawnUnitFromSnapshot(snap, GetFallbackModelPath, out string modelPath, out int playerIndex);
		if (entity == Entity.Null) return;
		var unit3D = SpawnUnit3D(entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, playerIndex);
		unit3D.Rotation = new Vector3(0f, snap.RotationY, 0f);
		if (_pendingSelectUnitServerId == snap.EntityId && GodotObject.IsInstanceValid(unit3D))
		{
			ClearSelection();
			SelectUnit(unit3D);
			GameContext.InGameHud?.RefreshUI(SelectedUnits);
			_pendingSelectUnitServerId = -1;
		}
	}

	private void QueueOrSendSnapshot(int peerId, byte[] payload, bool isBaseline)
	{
		if (GameContext.NetworkService.IsSpectatorWithDelay(peerId))
		{
			GameContext.NetworkService.QueueSpectatorDelayedPacket(new DelayedPacket
			{
				PeerId = peerId,
				Kind = isBaseline ? DelayedPacketKind.Baseline : DelayedPacketKind.Snapshot,
				SnapshotPayload = payload
			});
			return;
		}

		SendPresentationPacket(peerId, isBaseline ? DelayedPacketKind.Baseline : DelayedPacketKind.Snapshot, payload, null, default);
	}

	private void QueueOrSendSpellEffect(int peerId, string spellId, Vector3 position)
	{
		if (GameContext.NetworkService.IsSpectatorWithDelay(peerId))
		{
			GameContext.NetworkService.QueueSpectatorDelayedPacket(new DelayedPacket
			{
				PeerId = peerId,
				Kind = DelayedPacketKind.SpellEffect,
				SpellId = spellId,
				Position = position
			});
			return;
		}

		SendPresentationPacket(peerId, DelayedPacketKind.SpellEffect, null, spellId, position);
	}

	/// <summary>
	/// The queue a delayed spectator's packets wait in. It is drained on the snapshot tick, which is also what makes the
	/// hold-back real: the queue used to be filled and never read, so enabling the delay stopped a spectator receiving
	/// anything at all.
	/// </summary>
	private void ProcessDelayedSpectatorPackets()
	{
		var readyPackets = GameContext.NetworkService.FlushReadySpectatorPackets();
		for (int index = 0; index < readyPackets.Count; index++)
		{
			DelayedPacket packet = readyPackets[index];
			SendPresentationPacket(packet.PeerId, packet.Kind, packet.SnapshotPayload, packet.SpellId, packet.Position);
		}
	}

	private void SendPresentationPacket(int peerId, DelayedPacketKind kind, byte[]? payload, string? spellId, Vector3 position)
	{
		switch (kind)
		{
			case DelayedPacketKind.Baseline:
				RpcId(peerId, nameof(ReceiveBaselineSnapshot), payload);
				break;
			case DelayedPacketKind.Snapshot:
				SendSnapshotPayload(peerId, payload!);
				break;
			case DelayedPacketKind.SpellEffect:
				RpcId(peerId, nameof(PlaySpellEffect), spellId, position);
				break;
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void NetworkPingMinimap(Vector3 position)
	{
		AddMinimapPing(position);
	}

	public void TogglePauseRequest()
	{
		if (_multiplayerActive)
		{
			RpcId(1, nameof(RequestPause), Multiplayer.GetUniqueId());
		}
		else
		{
			GameContext.NetworkService.ToggleLocalPause();
			UpdatePauseUI();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestPause(int peerId)
	{
		GameContext.NetworkService.ProcessRequestPause(
			peerId,
			Multiplayer.IsServer(),
			(paused, byPeerId, instant) => Rpc(nameof(BroadcastPauseState), paused, byPeerId, instant),
			StartCountdown
		);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestToggleReady(int peerId, bool ready)
	{
		GameContext.NetworkService.ProcessRequestToggleReady(
			peerId, ready, Multiplayer.IsServer(),
			CheckAllPlayersReady,
			StartCountdown,
			(serialized) => Rpc(nameof(BroadcastReadyStates), serialized),
			() => Rpc(nameof(BroadcastCountdownCancel))
		);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestSetDisallowPause(int peerId, bool disallowed)
	{
		GameContext.NetworkService.ProcessRequestSetDisallowPause(
			peerId, disallowed, Multiplayer.IsServer(), Multiplayer.GetRemoteSenderId(),
			(serialized) => Rpc(nameof(BroadcastDisallowedPausePeers), serialized)
		);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RequestForceResume()
	{
		GameContext.NetworkService.ProcessRequestForceResume(
			Multiplayer.IsServer(), Multiplayer.GetRemoteSenderId(), StartCountdown
		);
	}

	private void StartCountdown(bool forcedByHost)
	{
		GameContext.NetworkService.ProcessStartCountdown(
			forcedByHost,
			(seconds, forced) => Rpc(nameof(BroadcastCountdownState), seconds, forced)
		);
	}

	private bool CheckAllPlayersReady()
	{
		return GameContext.NetworkService.ProcessCheckAllPlayersReady(GameContext.Lobby?.PlayerList);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastPauseState(bool paused, int pausedByPeerId, bool instantResume)
	{
		GameContext.NetworkService.ProcessBroadcastPauseState(
			paused, instantResume, UpdatePauseUI
		);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastReadyStates(string serializedReadyStates)
	{
		GameContext.NetworkService.ProcessBroadcastReadyStates(
			serializedReadyStates, UpdatePauseUI
		);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastDisallowedPausePeers(string serializedDisallowed)
	{
		GameContext.NetworkService.ProcessBroadcastDisallowedPausePeers(
			serializedDisallowed, UpdatePauseUI
		);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastCountdownState(int seconds, bool forcedByHost)
	{
		GameContext.NetworkService.ProcessBroadcastCountdownState(
			seconds, forcedByHost, UpdatePauseUI
		);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void BroadcastCountdownCancel()
	{
		GameContext.NetworkService.BroadcastCountdownCancelDomain();
		UpdatePauseUI();
	}

	public void GetPlayerReadyState(int peerId, out bool ready)
	{
		GameContext.NetworkService.GetPlayerReadyStateDomain(peerId, out ready);
	}

	public void GetPlayerDisallowPause(int peerId, out bool disallowed)
	{
		GameContext.NetworkService.GetPlayerDisallowPauseDomain(peerId, out disallowed);
	}

	private void UpdatePauseUI()
	{
		GameContext.InGameHud?.UpdatePauseUI();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncEnvironmentPresetRpc(string presetId, float durationSeconds)
	{
		if (durationSeconds <= 0.001f)
		{
			GameContext.EnvironmentService?.ApplyPresetById(presetId);
		}
		else
		{
			GameContext.EnvironmentService?.TransitionToPreset(presetId, durationSeconds);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SyncWeatherRpc(string weatherType)
	{
		GameContext.EnvironmentService?.SetCurrentWeather(weatherType);
		GameContext.InGameHud?.ApplyWeatherEffects(weatherType);
	}

	private void SendReconnectedBaselineSnapshot(int newPeerId)
	{
		var baselinePayload = GameContext.NetworkService.BuildExplicitBaselineSnapshot(newPeerId, AllUnits);
		if (baselinePayload != null && baselinePayload.Length > 0)
		{
			SendNavMeshObstacleVolumesToPeer(newPeerId);
			RpcId(newPeerId, nameof(ReceiveBaselineSnapshot), baselinePayload);
		}

		GameContext.NetworkService.RecordReconnectBaselineForDiagnostics(AllUnits, newPeerId);
	}

	private void SyncReconnectedCountdown(int newPeerId)
	{
		if (GameContext.NetworkService.TryGetReconnectedCountdown(out float duration, out string text))
		{
			RpcId(newPeerId, nameof(ClientStartCountdownTimer), duration, text);
		}
	}

	private void RegisterReconnectedAbilities(int newPeerId)
	{
		foreach (var kvp in GameContext.RegistryService.AbilityDefinitions)
		{
			RpcId(newPeerId, nameof(ClientRegisterAbility), kvp.Key, kvp.Value.DisplayName, kvp.Value.TooltipTemplate, kvp.Value.IconPath ?? "", kvp.Value.IsInstant);
		}
	}

	private void SyncReconnectedPauseState(int newPeerId)
	{
		if (!GameContext.NetworkService.IsPaused) return;

		RpcId(newPeerId, nameof(BroadcastPauseState), true, 1, false);
		var serializedReady = JsonSerializer.Serialize(GameContext.NetworkService.PlayerReadyStates);
		RpcId(newPeerId, nameof(BroadcastReadyStates), serializedReady);
	}

	public void HandlePeerReconnected(int oldPeerId, int newPeerId, int slot)
	{
		if (!Multiplayer.IsServer()) return;

		GD.Print($"[GameHost] HandlePeerReconnected: OldPeer={oldPeerId}, NewPeer={newPeerId}, Slot={slot}");

		Entity playerEntity = GameContext.NetworkService.ResolveReconnectedPlayerEntity(oldPeerId, newPeerId, _peerIdToPlayerEntityMap);
		GameContext.NetworkService.UpdateReconnectedNetworkMapping(oldPeerId, newPeerId, playerEntity);
		GameContext.NetworkService.ResetClientSnapshotState(oldPeerId);
		SendReconnectedBaselineSnapshot(newPeerId);
		SyncReconnectedCountdown(newPeerId);
		RegisterReconnectedAbilities(newPeerId);
		SyncReconnectedPauseState(newPeerId);
		SyncReconnectedCameraBounds(newPeerId, slot);
		SyncReconnectedHudVisibility(newPeerId, slot);
	}

	private void SyncReconnectedCameraBounds(int newPeerId, int slot)
	{
		if (_playerCameraBoundsOverrides.TryGetValue(slot, out var bounds))
		{
			RpcId(newPeerId, nameof(ClientSetCameraBounds), bounds.MinX, bounds.MinZ, bounds.MaxX, bounds.MaxZ);
		}
	}

	private void SyncReconnectedHudVisibility(int newPeerId, int slot)
	{
		if (_playerHudVisibilityOverrides.TryGetValue(slot, out var dict))
		{
			foreach (var kvp in dict)
			{
				RpcId(newPeerId, nameof(ClientSetHudElementVisible), (int)kvp.Key, kvp.Value);
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientRegisterAbility(string abilityId, string displayName, string tooltip, string iconPath, bool isInstant)
	{
		if (GameContext.RegistryService.RegisterAbility(abilityId, displayName, tooltip, iconPath, isInstant))
		{
			GameContext.InGameHud?.RefreshUI(SelectedUnits);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientShowFeedbackText(string text, Vector3 color)
	{
		if (GameContext.InGameHud != null)
		{
			var gColor = new Color(color.X, color.Y, color.Z);
			Callable.From(() => GameContext.InGameHud?.ShowFeedbackText(text, gColor)).CallDeferred();
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ClientCreateFloatingText(string text, Vector3 position, Vector3 color, float duration)
	{
		CreateFloatingTextInternal(text, position, new Color(color.X, color.Y, color.Z), duration);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientStartCountdownTimer(float duration, string label)
	{
		Callable.From(() => GameContext.InGameHud?.StartCountdownTimer(duration, label)).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientStopCountdownTimer()
	{
		Callable.From(() => GameContext.InGameHud?.StopCountdownTimer()).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientPanCameraTo(Vector3 position, float duration)
	{
		Callable.From(() => { PanCameraInternal(position, duration); }).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientShakeCamera(float intensity, float duration)
	{
		ShakeCameraInternal(intensity, duration);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSetCameraBounds(float minX, float minZ, float maxX, float maxZ)
	{
		Callable.From(() => SetCameraBoundsInternal(minX, minZ, maxX, maxZ)).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientResetCameraBounds()
	{
		Callable.From(() => ResetCameraBoundsInternal()).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSetHudElementVisible(int elementInt, bool visible)
	{
		Callable.From(() =>
		{
			HudElement element = (HudElement)elementInt;
			SetHudElementVisibleInternal(element, visible);
		}).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSelectUnit(int serverEntityId)
	{
		Callable.From(() =>
		{
			Entity targetEntity = Entity.Null;
			if (!TryGetLocalEntity(serverEntityId, out targetEntity))
			{
				foreach (var u in AllUnits)
				{
					if (GodotObject.IsInstanceValid(u) && u.Entity.Id == serverEntityId)
					{
						targetEntity = u.Entity;
						break;
					}
				}
			}

			if (targetEntity != Entity.Null && TryGetUnit3D(targetEntity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				ClearSelection();
				SelectUnit(unit3D);
				GameContext.InGameHud?.RefreshUI(SelectedUnits);
			}
			else
			{
				_pendingSelectUnitServerId = serverEntityId;
			}
		}).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientClearSelection()
	{
		ClearSelectionInternal();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientPingMinimap(Vector3 position)
	{
		Callable.From(() => AddMinimapPing(position)).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientPlayWarningSound()
	{
		Callable.From(() => GameContext.AudioService?.PlayWarningSound()).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientPlayClickSound()
	{
		Callable.From(() => GameContext.AudioService?.PlayClickSound()).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientStartBuildingPlacement(string unitTypeId)
	{
		Callable.From(() => EnterBuildingPlacement(unitTypeId)).CallDeferred();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientGameOver(bool isVictory)
	{
		GameContext.NetworkService.SetGameOver();
		Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, isVictory)).CallDeferred();
	}

	public void OnClientReconnected(int slot)
	{
		_snapshotReassembly.Clear();
		_newestSnapshotFragmentGroupId = 0;
		_localPeerId = Multiplayer.GetUniqueId();
		GameContext.NetworkService.LocalPeerId = _localPeerId;
		GameContext.NetworkService.MarkClientEnteredMultiplayer();
		GameContext.NetworkService.ResetReconnectionState();

		Entity reconnectedEntity = Entity.Null;
		if (GameContext.Lobby != null && GameContext.Lobby.PlayerList.Count > 0)
		{
			reconnectedEntity = GameContext.NetworkService.AssignReconnectedPlayerEntity(slot, GameContext.Lobby.PlayerList, _localPeerId, _peerIdToPlayerEntityMap);
		}

		GameContext.NetworkService.UpdateReconnectedClientMapping(reconnectedEntity, _localPeerId);

		if (IsMultiplayerActive() && !IsMultiplayerServerActive())
		{
			RpcId(1, nameof(ServerRequestDynamicPathingSync));
		}

		GameContext.InGameHud?.RefreshUI(SelectedUnits);
		GD.Print($"[GameHost] OnClientReconnected completed for LocalPeerId={_localPeerId}, Slot={slot}");
	}

	private readonly List<(float minX, float minZ, float maxX, float maxZ, int flags, bool blocked)> _dynamicPathingOverrides = new();
	private int _regionPathingSequence;
	private int _appliedRegionPathingSequence;

	private int NextRegionPathingSequence()
	{
		_regionPathingSequence++;
		_appliedRegionPathingSequence = _regionPathingSequence;
		return _regionPathingSequence;
	}

	public void ApplyRegionPathingInternal(float minX, float minZ, float maxX, float maxZ, int flags, bool blocked)
	{
		_dynamicPathingOverrides.Add((minX, minZ, maxX, maxZ, flags, blocked));
		var world = GameContext.World;
		var worldEntity = ECSContext.WorldEntity;
		if (world != null && world.IsAlive(worldEntity) && world.Has<TerrainState>(worldEntity))
		{
			ref var state = ref world.Get<TerrainState>(worldEntity);
			ECSContext.TerrainNavMeshService?.SetRegionPathing(ref state, minX, minZ, maxX, maxZ, flags, blocked);
			world.Set(worldEntity, state);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientUpdateRegionPathing(float minX, float minZ, float maxX, float maxZ, int flags, bool blocked, int sequence)
	{
		if (sequence <= _appliedRegionPathingSequence) return;

		_appliedRegionPathingSequence = sequence;
		ApplyRegionPathingInternal(minX, minZ, maxX, maxZ, flags, blocked);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ServerRequestDynamicPathingSync()
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		if (senderId <= 1) return;

		var sb = new System.Text.StringBuilder();
		for (int i = 0; i < _dynamicPathingOverrides.Count; i++)
		{
			var o = _dynamicPathingOverrides[i];
			if (i > 0) sb.Append(';');
			sb.Append($"{o.minX:F2},{o.minZ:F2},{o.maxX:F2},{o.maxZ:F2},{o.flags},{(o.blocked ? 1 : 0)}");
		}
		RpcId(senderId, nameof(ClientSyncDynamicPathingOverrides), _regionPathingSequence, sb.ToString());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ClientSyncDynamicPathingOverrides(int sequence, string serializedData)
	{
		if (sequence > _appliedRegionPathingSequence) _appliedRegionPathingSequence = sequence;
		if (string.IsNullOrEmpty(serializedData)) return;
		var parts = serializedData.Split(';', StringSplitOptions.RemoveEmptyEntries);
		foreach (var part in parts)
		{
			var tokens = part.Split(',');
			if (tokens.Length >= 6 &&
			    float.TryParse(tokens[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x1) &&
			    float.TryParse(tokens[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z1) &&
			    float.TryParse(tokens[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x2) &&
			    float.TryParse(tokens[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z2) &&
			    int.TryParse(tokens[4], out int flags) &&
			    int.TryParse(tokens[5], out int blockedInt))
			{
				ApplyRegionPathingInternal(x1, z1, x2, z2, flags, blockedInt != 0);
			}
		}
	}
}