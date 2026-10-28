using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.VFX;
using Realm.Ecs;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Replication;
using Realm.Ecs.Services;
using Realm.Shared.Metadata;
using Realm.Shared.Network;

namespace Realm.Client.Core;

/// <summary>
///     The client's end of a match: it holds the connection to the process that runs the world, hands that process this
///     player's orders, and applies what it is told. Nothing here simulates, so a match this machine started and a
///     match someone else runs reach the screen through the same calls.
/// </summary>
public partial class GameHost
{
	/// <summary>
	///     The match this screen joined. A match belongs to the session that was started for it rather than to whichever
	///     screen is showing it, so a screen that leaves while another takes the match over - the map editor handing a
	///     playtest to the screen that plays it - must not end a match it never joined.
	/// </summary>
	private ClientServerSession? _joinedSession;

	/// <summary>
	///     Holds the connection the match was started with. The authority frames that arrived before this scene existed
	///     are still queued in the session, so nothing the authority said while the map was loading is lost.
	/// </summary>
	private void JoinAuthority()
	{
		ClientServerSession? session = GameContext.ServerSession;
		if (session == null) return;

		_joinedSession = session;
		session.Seated += ApplyRosterAssignment;
		session.SnapshotReceived += ApplyReceivedSnapshot;
		session.PresentationReceived += ApplyPresentation;
		session.NavMeshObstaclesReceived += ApplyNavMeshObstacles;
		session.PauseStateReceived += ApplyMatchPause;
		session.CommandAnswered += ApplyCommandOutcome;
		session.LatencyProbeAnswered += GameContext.NetworkLatencyService.OnResponse;
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

		// Both of these are sent the moment the authority seats this connection, which is before this screen exists, so
		// the newest of each is applied here rather than waiting for a second copy that is never sent.
		if (session.LatestNavMeshObstacles is { } obstacles) ApplyNavMeshObstacles(obstacles);
		if (session.LatestPauseState is { } pause) ApplyMatchPause(pause);
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

		// The seat is the authority's answer to who this client plays as, so the screens read it as soon as it arrives
		// rather than keeping whatever the lobby guessed before the match started.
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

	/// <summary>
	///     What the authority decided about one of this player's orders. A command it refused is rolled back, because the
	///     order was predicted as soon as it was issued.
	/// </summary>
	private void ApplyCommandOutcome(CommandOutcome outcome)
	{
		if (!outcome.Accepted) RefusedCommandCount++;

		DeliverLocalCommandAck(outcome.CommandId, Array.Empty<int>(), outcome.Accepted ? string.Empty : outcome.RejectionReason ?? string.Empty);
	}

	/// <summary>How many of this client's orders the authority refused, which is what a harness asserts a flood runs into.</summary>
	public int RefusedCommandCount { get; private set; }

	/// <summary>
	///     Applies what the authority decided about one order. An order that was predicted as soon as it was issued is
	///     rolled back here, because the world this client is showing is not the world the authority kept.
	/// </summary>
	/// <param name="commandId">Which order the authority answered.</param>
	/// <param name="rejectedServerEntityIds">The units the authority refused, which are the ones that must be restored.</param>
	/// <param name="rejectionReason">Why the order was refused, or nothing when it was accepted.</param>
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
	///     Replaces this screen's obstacle volumes with the ones the match pathing was baked with and rebakes from them.
	///     A map's placed props are in both worlds because both load the same files; what this adds is whatever the map's
	///     script spawned, which this screen never ran and would otherwise path straight through. The volumes are kept so
	///     every later bake uses them too, including one that runs after this world was replaced.
	/// </summary>
	private void ApplyNavMeshObstacles(NavMeshObstacleVolumes volumes)
	{
		if (volumes.Positions == null || volumes.Radii == null) return;

		int count = Math.Min(volumes.Positions.Length, volumes.Radii.Length);
		var positions = new System.Numerics.Vector3[count];
		for (int index = 0; index < count; index++)
		{
			NetworkVector3 position = volumes.Positions[index];
			positions[index] = new System.Numerics.Vector3(position.X, position.Y, position.Z);
		}

		var radii = new float[count];
		Array.Copy(volumes.Radii, radii, count);

		_authoritativeObstaclePositions = positions;
		_authoritativeObstacleRadii = radii;
		_hasAuthoritativeObstacleVolumes = true;

		ApplyStoredObstacleVolumes();
		RebakeNavMesh();
	}

	private System.Numerics.Vector3[] _authoritativeObstaclePositions = System.Array.Empty<System.Numerics.Vector3>();
	private float[] _authoritativeObstacleRadii = System.Array.Empty<float>();
	private bool _hasAuthoritativeObstacleVolumes;

	/// <summary>
	///     Hands the volumes the match baked with to this screen's navigation mesh before it bakes, so every bake this
	///     screen makes uses the authority's own obstacle set instead of collecting its own. Until the authority's first
	///     set arrives this screen has nothing to defer to and bakes from the entities it loaded itself.
	/// </summary>
	private void ApplyStoredObstacleVolumes()
	{
		if (!_hasAuthoritativeObstacleVolumes) return;

		TerrainNavMeshService? navMesh = GameContext.Simulation.TerrainNavMeshService;
		if (navMesh == null) return;

		navMesh.SetAuthoritativeObstacleVolumes(_authoritativeObstaclePositions, _authoritativeObstacleRadii);
	}

	/// <summary>
	///     Shows the pause state the match holds. The clock belongs to the process running the match, so what this screen
	///     shows is what that process said rather than anything it decided itself.
	/// </summary>
	private void ApplyMatchPause(MatchPauseState state)
	{
		var network = GameContext.NetworkService;
		if (network == null) return;

		network.IsPaused = state.Paused;
		network.PausedByPlayerIndex = state.Paused ? state.RequestedByPlayer : -1;
		UpdatePauseUI();
	}

	/// <summary>
	///     Applies something the authority's map script asked a screen to show, in the words and the ids it already
	///     agreed on. Requests addressed to another player are ignored: what one player is told is not another's to see.
	///     Every kind the scripting surface can produce is answered here, so a map that draws, plays or writes something
	///     reaches this screen the same way whether this machine started the match or joined it.
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

			case PresentationKind.FloatingText:
				PresentFloatingText(message, position, color);
				break;

			case PresentationKind.MinimapPing:
				clientRpc.PingMinimap(message.PlayerIndex, position);
				break;

			case PresentationKind.TargetIndicator:
				PresentTargetIndicator(position, color);
				break;

			case PresentationKind.PlayerDefeated:
				clientRpc.TriggerDefeat(message.PlayerIndex, message.Text);
				break;

			case PresentationKind.PlayerVictory:
				clientRpc.TriggerVictory(message.PlayerIndex);
				break;

			case PresentationKind.VisualEffect:
				SpawnVisualEffectViewLogic(message.Key, position, message.Value > 0f ? message.Value : 1f);
				break;

			case PresentationKind.BarrageVolley:
				PresentBarrageVolley(message);
				break;

			case PresentationKind.GroundRing:
				PresentShockwave(message, position, ShockwaveType.ExpandingGroundRing);
				break;

			case PresentationKind.BurstSphere:
				PresentShockwave(message, position, ShockwaveType.ExpandingBurstSphere);
				break;

			case PresentationKind.TerrainLine:
				PresentTerrainLine(message);
				break;

			case PresentationKind.PersistentAura:
				PresentPersistentAura(message);
				break;

			case PresentationKind.Projectile:
				PresentProjectile(message);
				break;

			case PresentationKind.StaticTextCreated:
			case PresentationKind.StaticTextUpdated:
			case PresentationKind.StaticTextColor:
			case PresentationKind.StaticTextVisible:
			case PresentationKind.StaticTextDestroyed:
				PresentStaticText(message);
				break;

			case PresentationKind.LeaderboardVisible:
			case PresentationKind.LeaderboardValue:
			case PresentationKind.LeaderboardCleared:
			case PresentationKind.LeaderboardBoard:
			case PresentationKind.LeaderboardBoardCleared:
			case PresentationKind.LeaderboardBoardGroup:
			case PresentationKind.LeaderboardBoardRow:
			case PresentationKind.LeaderboardRow:
				PresentLeaderboard(message);
				break;

			case PresentationKind.SummaryTableVisible:
			case PresentationKind.SummaryTableHeaders:
			case PresentationKind.SummaryTableRow:
			case PresentationKind.SummaryTableCleared:
				PresentSummaryTable(message);
				break;

			case PresentationKind.CountdownStarted:
				ClientStartCountdownTimer(message.Value, message.Key);
				break;

			case PresentationKind.CountdownStopped:
				ClientStopCountdownTimer();
				break;

			case PresentationKind.CountdownLabel:
				SetCountdownTimerLabelView(message.Key);
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

			case PresentationKind.TimeOfDaySet:
				SetTimeOfDayViewLogic(message.Value);
				break;

			case PresentationKind.DayNightCycleSet:
				SetDayNightCycleEnabledViewLogic(message.Flag);
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

			case PresentationKind.UnitColored:
				PresentUnitColor(message);
				break;

			case PresentationKind.UnitAnimationSet:
				PresentUnitAnimation(message);
				break;

			case PresentationKind.UnitHandAttachmentSet:
				PresentUnitHandAttachment(message);
				break;

			case PresentationKind.UnitMeshImpulse:
				PresentUnitImpulse(message);
				break;

			case PresentationKind.ResourceMeshImpulse:
				PresentResourceImpulse(message);
				break;

			case PresentationKind.PlayerAbilityRegistered:
			case PresentationKind.AbilityRegistered:
				ClientRegisterAbility(message.Key, message.Text, message.Text2, message.Text3, message.Flag);
				break;

			case PresentationKind.AbilityInstantSet:
				PresentAbilityDetail(message, () => GameContext.RegistryService.SetAbilityInstant(message.Key, message.Count != 0));
				break;

			case PresentationKind.AbilityIconSet:
				PresentAbilityDetail(message, () => GameContext.RegistryService.SetAbilityIcon(message.Key, message.Text));
				break;

			case PresentationKind.AbilityTooltipSet:
				PresentAbilityDetail(message, () => GameContext.RegistryService.SetAbilityTooltip(message.Key, message.Text));
				break;

			case PresentationKind.AbilityHotkeySet:
				PresentAbilityDetail(message, () => GameContext.RegistryService.SetAbilityHotkey(message.Key, message.Text));
				break;

			case PresentationKind.AbilityGridPositionSet:
				PresentAbilityDetail(message, () => GameContext.RegistryService.SetAbilityGridPosition(message.Key, message.Count, message.SecondCount));
				break;

			case PresentationKind.ItemTooltipSet:
				PresentItemTooltip(message);
				break;

			case PresentationKind.TooltipNumberSet:
				GameContext.RegistryService.TooltipValues.SetNumber(
					TooltipValueStore.OwnerKey(message.Key, message.Text), message.Text2, message.Value);
				break;

			case PresentationKind.TooltipTextSet:
				GameContext.RegistryService.TooltipValues.SetText(
					TooltipValueStore.OwnerKey(message.Key, message.Text), message.Text2, message.Text3);
				break;

			case PresentationKind.TechLevelSet:
				GameContext.TechTreeService?.ApplyReplicatedLevel(message.PlayerIndex, message.Key, (int)message.Value);
				break;

			case PresentationKind.CastSound:
				PresentCastSound(message, position);
				break;

			case PresentationKind.PlayerKicked:
				clientRpc.ShowFeedbackText(message.PlayerIndex, message.Text, color);
				break;

			default:
				GD.PushWarning($"[Presentation] Kind {message.Kind} has no handler on this screen.");
				break;
		}
	}

	/// <summary>
	///     Resolves the unit a request names to the node this screen drew for it. The authority names units by the entity
	///     id it owns, so the id has to be mapped before anything can be shown on the unit it means.
	/// </summary>
	private bool TryPresentUnit(int serverUnitId, out Unit3D unit3D)
	{
		unit3D = null!;
		if (serverUnitId <= 0) return false;
		if (!TryGetLocalEntity(serverUnitId, out Entity entity)) return false;
		if (GameContext.World == null || !GameContext.World.IsAlive(entity)) return false;
		if (!TryGetUnit3D(entity, out Unit3D found) || !GodotObject.IsInstanceValid(found)) return false;

		unit3D = found;
		return true;
	}

	private void PresentFloatingText(PresentationMessage message, System.Numerics.Vector3 position, System.Numerics.Vector3 color)
	{
		if (string.IsNullOrEmpty(message.Text)) return;

		CreateFloatingTextInternal(
			message.Text,
			new Vector3(position.X, position.Y, position.Z),
			new Color(color.X, color.Y, color.Z),
			message.Duration > 0f ? message.Duration : 1.5f);
	}

	private void PresentTargetIndicator(System.Numerics.Vector3 position, System.Numerics.Vector3 color)
	{
		SpawnTargetIndicator(new Vector3(position.X, position.Y, position.Z), new Color(color.X, color.Y, color.Z));
	}

	private void PresentBarrageVolley(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;

		string effectTypeId = message.Key;
		var center = new Vector3(message.Position.X, message.Position.Y, message.Position.Z);
		int impacts = Math.Clamp(message.Count, 1, 64);
		float spacing = MathF.Max(0.01f, message.Duration);
		float spread = MathF.Max(0f, message.Radius);

		Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(this)) return;

			for (int index = 0; index < impacts; index++)
			{
				SceneTreeTimer timer = GetTree().CreateTimer(index * spacing);
				timer.Timeout += () =>
				{
					if (!GodotObject.IsInstanceValid(this)) return;

					float angle = GD.Randf() * MathF.Tau;
					float distance = GD.Randf() * spread;
					var impact = center + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
					SpawnVisualEffectViewLogic(effectTypeId, new System.Numerics.Vector3(impact.X, impact.Y, impact.Z), 1f);
				};
			}
		}).CallDeferred();
	}

	private void PresentShockwave(PresentationMessage message, System.Numerics.Vector3 position, ShockwaveType type)
	{
		var center = new Vector3(position.X, position.Y, position.Z);
		float maxRadius = message.Radius > 0f ? message.Radius : 8f;
		float speed = message.Value > 0f ? message.Value : 12f;
		float duration = message.Duration > 0f ? message.Duration : 1f;
		var waveColor = new Color(message.Color.X, message.Color.Y, message.Color.Z);

		Callable.From(() => GroundShockwave3D.Create(this, center, type, maxRadius, speed, duration, waveColor)).CallDeferred();
	}

	private void PresentTerrainLine(PresentationMessage message)
	{
		var start = new System.Numerics.Vector2(message.Position.X, message.Position.Z);
		var end = new System.Numerics.Vector2(message.Secondary.X, message.Secondary.Z);
		var lineColor = new Color(message.Color.X, message.Color.Y, message.Color.Z);
		float duration = message.Duration > 0f ? message.Duration : 1f;
		float width = message.Value > 0f ? message.Value : 0.2f;

		Callable.From(() => TerrainLine3D.Create(this, start, end, lineColor, duration, width)).CallDeferred();
	}

	private void PresentPersistentAura(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;
		if (!TryPresentUnit(message.Handle, out Unit3D unit3D)) return;

		string auraVfxId = message.Key;
		float scale = message.Value > 0f ? message.Value : 1f;

		Callable.From(() => unit3D.SetSocketAttachment(HumanoidBone.Chest, auraVfxId, null, null, scale, null, null, false)).CallDeferred();
	}

	private void PresentProjectile(PresentationMessage message)
	{
		var start = new Vector3(message.Position.X, message.Position.Y, message.Position.Z);
		var target = new Vector3(message.Secondary.X, message.Secondary.Y, message.Secondary.Z);

		// The authority names the projectile the map asked for; an id this screen does not know still draws, because the
		// effect service falls back to the arrow every map has.
		Callable.From(() => GameContext.FXService.SpawnWeaponProjectile(this, start, target, message.Key, default, message.PlayerIndex)).CallDeferred();
	}

	private void PresentStaticText(PresentationMessage message)
	{
		bool destroyed = (PresentationKind)message.Kind == PresentationKind.StaticTextDestroyed;

		ApplyStaticTextDelta(new StaticTextDelta
		{
			Handle = message.Handle,
			Text = message.Text,
			PositionX = message.Position.X,
			PositionY = message.Position.Y,
			PositionZ = message.Position.Z,
			ColorX = message.Color.X,
			ColorY = message.Color.Y,
			ColorZ = message.Color.Z,
			FontSize = message.Count > 0 ? message.Count : 48,
			Visible = message.Flag ? (byte)1 : (byte)0,
			Destroyed = destroyed ? (byte)1 : (byte)0
		});
	}

	private void PresentLeaderboard(PresentationMessage message)
	{
		switch ((PresentationKind)message.Kind)
		{
			case PresentationKind.LeaderboardVisible:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.SetBoard,
					BoardId = DefaultLeaderboardBoardId,
					Title = message.Key,
					Visible = message.Flag
				});
				break;

			case PresentationKind.LeaderboardValue:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.AddRow,
					BoardId = DefaultLeaderboardBoardId,
					GroupPlayerIndex = -1,
					Label = message.Key,
					Value = message.Text
				});
				break;

			case PresentationKind.LeaderboardCleared:
				QueueLeaderboardOperation(new LeaderboardOperation { Kind = LeaderboardOperationKind.ClearAll });
				break;

			case PresentationKind.LeaderboardBoard:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.SetBoard,
					BoardId = message.Key,
					Title = message.Text,
					Visible = message.Flag
				});
				break;

			case PresentationKind.LeaderboardBoardCleared:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.ClearBoard,
					BoardId = message.Key
				});
				break;

			case PresentationKind.LeaderboardBoardGroup:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.SetGroup,
					BoardId = message.Key,
					GroupPlayerIndex = message.Count,
					GroupTitle = message.Text
				});
				break;

			case PresentationKind.LeaderboardBoardRow:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.AddRow,
					BoardId = message.Key,
					GroupPlayerIndex = message.Count,
					GroupTitle = message.Text,
					Label = message.Text2,
					Value = message.Text3
				});
				break;

			case PresentationKind.LeaderboardRow:
				QueueLeaderboardOperation(new LeaderboardOperation
				{
					Kind = LeaderboardOperationKind.AddRow,
					BoardId = string.IsNullOrWhiteSpace(message.Key) ? DefaultLeaderboardBoardId : message.Key,
					GroupPlayerIndex = -1,
					Label = message.Text,
					Value = message.Text2
				});
				break;
		}
	}

	private void PresentSummaryTable(PresentationMessage message)
	{
		string title = message.Key;
		string rowKey = message.Key;
		string[] values = message.Values ?? Array.Empty<string>();
		bool visible = message.Flag;
		PresentationKind kind = (PresentationKind)message.Kind;

		Callable.From(() =>
		{
			switch (kind)
			{
				case PresentationKind.SummaryTableVisible:
					GameContext.InGameHud?.ShowSummaryTable(title, visible);
					break;
				case PresentationKind.SummaryTableHeaders:
					GameContext.InGameHud?.ClearSummaryTable();
					GameContext.InGameHud?.SetSummaryTableHeaders(values);
					break;
				case PresentationKind.SummaryTableRow:
					GameContext.InGameHud?.SetSummaryTableRow(rowKey, values);
					break;
				case PresentationKind.SummaryTableCleared:
					GameContext.InGameHud?.ClearSummaryTable();
					break;
			}
		}).CallDeferred();
	}

	private void PresentUnitColor(PresentationMessage message)
	{
		if (!TryPresentUnit(message.Handle, out Unit3D unit3D)) return;

		var tint = new Color(message.Color.X, message.Color.Y, message.Color.Z);
		Callable.From(() => unit3D.ApplyModelTint(tint)).CallDeferred();
	}

	private void PresentUnitAnimation(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;
		if (!TryPresentUnit(message.Handle, out Unit3D unit3D)) return;

		string animationName = message.Key;
		Callable.From(() => unit3D.PlayAnimation(animationName)).CallDeferred();
	}

	private void PresentUnitHandAttachment(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;
		if (!TryPresentUnit(message.Handle, out Unit3D unit3D)) return;

		HumanoidBone hand = ResolveHandBone(message.Key);
		string? attachmentId = string.IsNullOrWhiteSpace(message.Text) ? null : message.Text;
		Callable.From(() => unit3D.SetHandAttachment(hand, attachmentId)).CallDeferred();
	}

	private static HumanoidBone ResolveHandBone(string hand)
	{
		if (hand.Equals("LeftHand", StringComparison.OrdinalIgnoreCase)
		    || hand.Equals("left", StringComparison.OrdinalIgnoreCase)
		    || hand.Equals("hand_l", StringComparison.OrdinalIgnoreCase))
		{
			return HumanoidBone.LeftHand;
		}

		return HumanoidBone.RightHand;
	}

	private void PresentUnitImpulse(PresentationMessage message)
	{
		if (!TryPresentUnit(message.Handle, out Unit3D unit3D)) return;

		float strength = message.Value > 0f ? message.Value : 1f;
		float duration = message.Duration > 0f ? message.Duration : 0.5f;
		float frequency = message.SecondCount > 0 ? message.SecondCount : 12f;

		Callable.From(() => unit3D.TriggerImpulse(strength, duration, frequency)).CallDeferred();
	}

	private void PresentResourceImpulse(PresentationMessage message)
	{
		if (message.Handle <= 0) return;
		if (!TryGetResourceProp((ResourceNodeId)message.Handle, out Prop3D? prop) || prop == null) return;

		float strength = message.Value > 0f ? message.Value : 1f;
		float duration = message.Duration > 0f ? message.Duration : 0.5f;
		float frequency = message.SecondCount > 0 ? message.SecondCount : 12f;

		Callable.From(() => prop.TriggerImpulse(strength, duration, frequency)).CallDeferred();
	}

	private void PresentAbilityDetail(PresentationMessage message, Action apply)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;

		apply();
		GameContext.InGameHud?.RefreshUI(SelectedUnits);
	}

	private static void PresentItemTooltip(PresentationMessage message)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;

		var registry = GameContext.RegistryService;
		if (registry == null) return;

		var itemId = new StringName(message.Key);
		if (registry.ItemRegistry.TryGetValue(itemId, out ItemMetadata? item)) item.Description = message.Text;
	}

	private void PresentCastSound(PresentationMessage message, System.Numerics.Vector3 position)
	{
		if (string.IsNullOrWhiteSpace(message.Key)) return;

		var place = new Vector3(position.X, position.Y, position.Z);
		string soundPath = message.Key;
		GameContext.AudioService?.PlaySound3D(soundPath, place);
	}
}
