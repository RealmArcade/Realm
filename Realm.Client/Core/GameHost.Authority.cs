using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Client.Core;

public partial class GameHost
{
	private ClientServerSession? _joinedSession;

	private readonly List<PresentationMessage> _pendingPresentation = new();

	private const int MaximumPendingPresentation = 512;

	private void JoinAuthority()
	{
		ClientServerSession? session = GameContext.ServerSession;
		if (session == null) return;

		_joinedSession = session;
		_pendingPresentation.Clear();
		session.Seated += ApplyRosterAssignment;
		session.SnapshotReceived += ApplyReceivedSnapshot;
		session.PresentationReceived += QueuePresentation;
		session.NavMeshObstaclesReceived += ApplyNavMeshObstacles;
		session.PauseStateReceived += ApplyMatchPause;
		session.PauseAllowanceReceived += ApplyPauseAllowance;
		session.CommandAnswered += ApplyCommandOutcome;
		session.LatencyProbeAnswered += GameContext.NetworkLatencyService.OnResponse;
		session.ServerLost += ReportAuthorityLost;
		session.ChatReceived += ApplyChatLine;

		// The seat and the obstacle volumes are sent the moment the authority seats this connection, which is before
		// this screen exists, so the newest of each is applied here rather than waiting for a copy that is never sent.
		// A connection seated with its volumes still on the way holds its first bake until they arrive, because the
		// mesh this screen paths through has to be the one the authority built.
		if (session.LocalPlayerSlot >= 0)
		{
			ApplyRosterAssignment(new PeerRosterAssignment
			{
				PeerId = session.LocalPeerId,
				PlayerSlot = session.LocalPlayerSlot,
				Team = session.LocalTeam
			});
		}

		if (session.LatestNavMeshObstacles is { } obstacles) ApplyNavMeshObstacles(obstacles);
		else AwaitAuthoritativeObstacleVolumes();
		if (session.LatestPauseState is { } pause) ApplyMatchPause(pause);
		if (session.LatestPauseAllowance is { } allowance) ApplyPauseAllowance(allowance);
	}

	public void EndJoinedMatch()
	{
		ClientServerSession? session = GameContext.ServerSession;
		if (session == null || !ReferenceEquals(session, _joinedSession)) return;

		_joinedSession = null;
		_pendingPresentation.Clear();
		StopAwaitingAuthoritativeObstacleVolumes();
		session.Stop();
		GameContext.DetachServerSession();
	}

	public void EndMatchAndClearWorld()
	{
		EndJoinedMatch();
		ResetWorldAndState();
	}

	private void ApplyChatLine(ChatLine line)
	{
		GameContext.Lobby?.ReceiveChatFromAuthority(line.Sender, line.Message, line.AlliesOnly, line.TimestampUtcTicks);
	}

	private void ApplyRosterAssignment(PeerRosterAssignment assignment)
	{
		_localPeerId = assignment.PeerId;
		GameContext.NetworkService.LocalPeerId = assignment.PeerId;
		GameContext.NetworkService.MarkClientEnteredMultiplayer();

		if (assignment.PlayerSlot > 0 && GameContext.ReplayPlayback is not { IsPlayingReplay: true })
		{
			GameContext.PerspectiveFilter.ConfigureParticipant(assignment.PlayerSlot);
		}

		var world = GameContext.World;
		Entity worldEntity = GameContext.Simulation.WorldEntity;
		if (world == null || worldEntity == Entity.Null || !world.IsAlive(worldEntity) || !world.Has<NetworkMappingState>(worldEntity)) return;

		NetworkMappingState mapping = world.Get<NetworkMappingState>(worldEntity);
		if (assignment.PlayerSlot < 0 || assignment.PlayerSlot >= mapping.PlayerEntities.Length) return;

		_peerIdToPlayerEntityMap[assignment.PeerId] = mapping.PlayerEntities[assignment.PlayerSlot];
	}

	private void ApplyCommandOutcome(CommandOutcome outcome)
	{
		if (outcome.Accepted) return;

		RefusedCommandCount++;
		ClearPreviewRoute(outcome.CommandId);

		// Taking the route back was the only sign a refused order gave. A player who placed something and watched nothing
		// happen had no way to tell a refusal from a click the match never heard.
		if (string.IsNullOrEmpty(outcome.RejectionReason)) return;

		GameContext.InGameHud?.ShowFeedbackText(TranslationServer.Translate(outcome.RejectionReason), new Color(1.0f, 0.3f, 0.25f));
	}

	public int RefusedCommandCount { get; private set; }

	private void ReportAuthorityLost(string report)
	{
		GD.PushError($"[GameHost] {report}");
		GameContext.NetworkService.ReportAuthorityLost();
		GameContext.InGameHud?.ShowFeedbackText(report, new Color(1.0f, 0.35f, 0.3f));
	}

}
