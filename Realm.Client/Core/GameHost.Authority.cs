using System;
using Godot;
using Realm.Client.Services;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.MapAPI;
using Realm.Shared.Network;

namespace Realm.Client.Core;

/// <summary>
///     The client's end of a match: it holds the connection to the process that runs the world, hands that process this
///     player's orders, and applies what it is told. Nothing here simulates, so a match this machine started and a
///     match someone else runs reach the screen through the same calls.
/// </summary>
public partial class GameHost
{
	private ClientServerSession? _authoritySession;

	/// <summary>
	///     Holds the connection the match was started with. The authority frames that arrived before this scene existed
	///     are still queued in the session, so nothing the authority said while the map was loading is lost.
	/// </summary>
	private void JoinAuthority()
	{
		ClientServerSession? session = GameContext.ServerSession;
		if (session == null) return;

		_authoritySession = session;
		session.Seated += ApplyRosterAssignment;
		session.SnapshotReceived += ApplyReceivedSnapshot;
		session.PresentationReceived += ApplyPresentation;
		session.CommandAnswered += ApplyCommandOutcome;
		session.ServerLost += ReportAuthorityLost;

		if (session.LocalPlayerSlot >= 0)
		{
			ApplyRosterAssignment(new PeerRosterAssignment
			{
				PeerId = session.LocalPeerId,
				PlayerSlot = session.LocalPlayerSlot,
				Team = session.LocalTeam
			});
		}
	}

	/// <summary>
	///     Takes the player the authority seated this connection as. The peer id the authority assigned is the one every
	///     order of this player is attributed to, so the roster is keyed by it rather than by whatever the game happened
	///     to assume before the authority answered.
	/// </summary>
	private void ApplyRosterAssignment(PeerRosterAssignment assignment)
	{
		_localPeerId = assignment.PeerId;
		GameContext.NetworkService.LocalPeerId = assignment.PeerId;
		GameContext.NetworkService.MarkClientEnteredMultiplayer();

		var world = GameContext.World;
		Entity worldEntity = GameContext.Simulation.WorldEntity;
		if (world == null || worldEntity == Entity.Null || !world.IsAlive(worldEntity) || !world.Has<NetworkMappingState>(worldEntity)) return;

		NetworkMappingState mapping = world.Get<NetworkMappingState>(worldEntity);
		if (assignment.PlayerSlot < 0 || assignment.PlayerSlot >= mapping.PlayerEntities.Length) return;

		_peerIdToPlayerEntityMap[assignment.PeerId] = mapping.PlayerEntities[assignment.PlayerSlot];
	}

	/// <summary>
	///     What the authority decided about one of this player's orders. A command it refused is rolled back, because the
	///     order was predicted as soon as it was issued.
	/// </summary>
	private void ApplyCommandOutcome(CommandOutcome outcome)
	{
		DeliverLocalCommandAck(outcome.CommandId, Array.Empty<int>(), outcome.Accepted ? string.Empty : outcome.RejectionReason ?? string.Empty);
	}

	/// <summary>
	///     Reports that the process running the match ended. The match stops rather than freezing on a world that will
	///     never advance again, and the reason names the log that explains why.
	/// </summary>
	private void ReportAuthorityLost(string report)
	{
		GD.PushError($"[GameHost] {report}");
		GameContext.NetworkService.ReportAuthorityLost();
		GameContext.InGameHud?.ShowFeedbackText(report, new Color(1.0f, 0.35f, 0.3f));
	}

	/// <summary>
	///     Applies something the authority's map script asked a screen to show, in the words and the ids it already
	///     agreed on. Requests addressed to another player are ignored: what one player is told is not another's to see.
	/// </summary>
	private void ApplyPresentation(PresentationMessage message)
	{
		if (message.PlayerIndex >= 0 && !GameContext.PerspectiveFilter.CanControl(message.PlayerIndex)) return;

		var position = new System.Numerics.Vector3(message.Position.X, message.Position.Y, message.Position.Z);
		var secondary = new System.Numerics.Vector3(message.Secondary.X, message.Secondary.Y, message.Secondary.Z);
		var color = new System.Numerics.Vector3(message.Color.X, message.Color.Y, message.Color.Z);
		ClientRpcService clientRpc = GameContext.ClientRPC;

		switch ((PresentationKind)message.Kind)
		{
			case PresentationKind.PlayerMessage:
				clientRpc.SendMessage(message.PlayerIndex, message.Text);
				break;

			case PresentationKind.BroadcastMessage:
				clientRpc.BroadcastMessage(message.Text);
				break;

			case PresentationKind.PlayerFeedbackText:
				clientRpc.ShowFeedbackText(message.PlayerIndex, message.Text, color);
				break;

			case PresentationKind.FeedbackText:
				clientRpc.BroadcastFeedbackText(message.Text, color);
				break;

			case PresentationKind.MinimapPing:
				clientRpc.PingMinimap(message.PlayerIndex, position);
				break;

			case PresentationKind.CameraPanned:
				clientRpc.PanCameraTo(message.PlayerIndex, position, message.Duration);
				break;

			case PresentationKind.CameraShaken:
				clientRpc.ShakeCamera(message.PlayerIndex, message.Value, message.Duration);
				break;

			case PresentationKind.CameraBoundsSet:
				clientRpc.SetPlayerCameraBounds(message.PlayerIndex, position.X, position.Z, secondary.X, secondary.Z);
				break;

			case PresentationKind.CameraBoundsReset:
				clientRpc.ResetPlayerCameraBounds(message.PlayerIndex);
				break;

			case PresentationKind.HudElementVisible:
				clientRpc.SetPlayerHudElementVisible(message.PlayerIndex, (HudElement)message.Count, message.Flag);
				break;

			case PresentationKind.SelectionChanged:
				if (message.Flag) ClientSelectUnit(message.Handle);
				else ClientClearSelection();
				break;

			case PresentationKind.WarningSound:
				clientRpc.PlayWarningSound(message.PlayerIndex);
				break;

			case PresentationKind.ClickSound:
				clientRpc.PlayClickSound(message.PlayerIndex);
				break;

			case PresentationKind.BuildingPlacementStarted:
				clientRpc.StartBuildingPlacement(message.PlayerIndex, message.Key);
				break;

			case PresentationKind.CountdownStarted:
				ClientStartCountdownTimer(message.Value, message.Key);
				break;

			case PresentationKind.CountdownStopped:
				ClientStopCountdownTimer();
				break;

			case PresentationKind.EnvironmentPresetSet:
				SyncEnvironmentPresetRpc(message.Key, 0f);
				break;

			case PresentationKind.EnvironmentPresetTransitioned:
				SyncEnvironmentPresetRpc(message.Key, message.Duration);
				break;

			case PresentationKind.WeatherSet:
				SyncWeatherRpc(message.Key);
				break;

			case PresentationKind.PlayerAbilityRegistered:
			case PresentationKind.AbilityRegistered:
				ClientRegisterAbility(message.Key, message.Text, message.Text2, message.Text3, message.Flag);
				break;

			case PresentationKind.PlayerDefeated:
				clientRpc.TriggerDefeat(message.PlayerIndex, message.Text);
				break;

			case PresentationKind.PlayerVictory:
				clientRpc.TriggerVictory(message.PlayerIndex);
				break;

			case PresentationKind.PlayerKicked:
				clientRpc.ShowFeedbackText(message.PlayerIndex, message.Text, color);
				break;

			case PresentationKind.Projectile:
				ClientSpawnArrowProjectile(new Vector3(position.X, position.Y, position.Z), new Vector3(secondary.X, secondary.Y, secondary.Z), message.PlayerIndex);
				break;
		}
	}
}
