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
	private void QueuePresentation(PresentationMessage message)
	{
		if (message.Sequence < 0)
		{
			ApplyPresentation(message);
			return;
		}

		if (_pendingPresentation.Count >= MaximumPendingPresentation) _pendingPresentation.RemoveAt(0);
		_pendingPresentation.Add(message);
	}

	private void FlushPresentationForCursor()
	{
		if (_pendingPresentation.Count == 0) return;

		float cursor = GameContext.SnapshotTimeline.CursorSequence;
		if (cursor < 0f) return;

		int kept = 0;
		for (int index = 0; index < _pendingPresentation.Count; index++)
		{
			PresentationMessage message = _pendingPresentation[index];
			if (message.Sequence <= cursor)
			{
				ApplyPresentation(message);
				continue;
			}

			_pendingPresentation[kept++] = message;
		}

		if (kept < _pendingPresentation.Count) _pendingPresentation.RemoveRange(kept, _pendingPresentation.Count - kept);
	}

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

			case PresentationKind.InstructionsSectionAdded:
				PresentInstructionsSection(message);
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
			case PresentationKind.UnitAnimationPulse:
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

			case PresentationKind.ResourceNodeDepleted:
				PresentResourceNodeDepleted(message);
				break;

			case PresentationKind.ResourceNodeAmountChanged:
				PresentResourceNodeAmount(message);
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
			case PresentationKind.TooltipTextSet:
				// A map declares the numbers its cards tooltips quote as the match starts, which can be before this screen
				// has a HUD holding a registry to publish them into, so they are held and replayed like the rest of what a
				// map declares about itself.
				HoldHudPresentation(message);
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

}
