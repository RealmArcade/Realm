using System;
using System.Collections.Generic;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.MapAPI;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

/// <summary>
/// Dedicated engine-layer service for all client-specific presentation and RPC actions
/// (camera movement, camera shake, unit selection, HUD feedback text, minimap pings,
/// client audio, building placement mode, and victory/defeat transitions).
/// All player-targeted methods require an explicit <c>playerIndex</c> parameter so the
/// target client is never implicitly assumed.
/// </summary>
public class ClientRpcService
{
	private static bool TryResolveTargetClient(int playerIndex, out GameHost host, out bool isLocal, out int remotePeerId)
	{
		host = GameContext.Host;
		isLocal = false;
		remotePeerId = -1;

		if (host == null || !GodotObject.IsInstanceValid(host))
			return false;

		if (playerIndex < 0)
			return false;

		// Never dispatch client UI/camera/selection actions for computer/AI slots.
		if (host.IsPlayerComputer(playerIndex))
			return false;

		if (GameContext.PerspectiveFilter.CanControl(playerIndex))
		{
			isLocal = true;
			return true;
		}

		if (host.IsMultiplayerServerActive() && GameContext.Lobby != null)
		{
			var playerInfo = GameContext.Lobby.PlayerList.Find(x => x.Slot == playerIndex);
			if (playerInfo != null && playerInfo.PeerId > 1)
			{
				remotePeerId = playerInfo.PeerId;
				return true;
			}
		}

		return false;
	}

	public void PanCameraTo(int playerIndex, Vector3 position, float duration = 0f)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		var godotPos = new Godot.Vector3(position.X, position.Y, position.Z);
		if (isLocal)
		{
			host.PanCameraInternal(godotPos, duration);
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientPanCameraTo), godotPos, duration);
		}
	}

	public void ShakeCamera(int playerIndex, float intensity, float duration)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			host.ShakeCameraInternal(intensity, duration);
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientShakeCamera), intensity, duration);
		}
	}

	public void BroadcastShakeCamera(float intensity, float duration)
	{
		var host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return;

		host.ShakeCameraInternal(intensity, duration);
		if (host.IsMultiplayerServerActive())
		{
			host.Rpc(nameof(GameHost.ClientShakeCamera), intensity, duration);
		}
	}

	public void SetPlayerCameraBounds(int playerIndex, float minX, float minZ, float maxX, float maxZ)
	{
		var host = GameContext.Host;
		if (host != null)
		{
			host.RecordPlayerCameraBoundsOverride(playerIndex, minX, minZ, maxX, maxZ);
		}

		if (!TryResolveTargetClient(playerIndex, out host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			host.SetCameraBoundsInternal(minX, minZ, maxX, maxZ);
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientSetCameraBounds), minX, minZ, maxX, maxZ);
		}
	}

	public void ResetPlayerCameraBounds(int playerIndex)
	{
		var host = GameContext.Host;
		if (host != null)
		{
			host.ClearPlayerCameraBoundsOverride(playerIndex);
		}

		if (!TryResolveTargetClient(playerIndex, out host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			host.ResetCameraBoundsInternal();
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientResetCameraBounds));
		}
	}

	public void SetPlayerHudElementVisible(int playerIndex, HudElement element, bool visible)
	{
		var host = GameContext.Host;
		if (host != null)
		{
			host.RecordPlayerHudVisibility(playerIndex, element, visible);
		}

		if (!TryResolveTargetClient(playerIndex, out host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			host.SetHudElementVisibleInternal(element, visible);
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientSetHudElementVisible), (int)element, visible);
		}
	}

	public void SelectUnit(int playerIndex, UnitId unit)
	{
		if (!unit.IsValid)
			return;

		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			host.SelectUnitInternal(unit);
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientSelectUnit), unit.Value);
		}
	}

	public void ClearSelection(int playerIndex)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			host.ClearSelectionInternal();
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientClearSelection));
		}
	}

	public void SendMessage(int playerIndex, string message)
	{
		string formatted = $"[HOST MESSAGE P{playerIndex}] {message}";
		WasmRuntime.LogToConsole(formatted);

		ShowFeedbackText(playerIndex, message, new Vector3(0.9f, 0.9f, 0.9f));
	}

	public void BroadcastMessage(string message)
	{
		var host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return;

		GD.Print($"[HOST BROADCAST] {message}");
		BroadcastFeedbackText(message, new Vector3(0.9f, 0.9f, 0.9f));
		GameContext.NetworkService?.BroadcastMessageDomainLogic(message, host.IsMultiplayerActive(), host.IsMultiplayerServerActive());
	}

	public void ShowFeedbackText(int playerIndex, string text, Vector3 color)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			if (GameContext.InGameHud != null)
			{
				var gColor = new Color(color.X, color.Y, color.Z);
				Callable.From(() => GameContext.InGameHud?.ShowFeedbackText(text, gColor)).CallDeferred();
			}
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientShowFeedbackText), text, new Godot.Vector3(color.X, color.Y, color.Z));
		}
	}

	public void BroadcastFeedbackText(string text, Vector3 color)
	{
		var host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return;

		if (GameContext.InGameHud != null)
		{
			var gColor = new Color(color.X, color.Y, color.Z);
			Callable.From(() => GameContext.InGameHud?.ShowFeedbackText(text, gColor)).CallDeferred();
		}

		if (host.IsMultiplayerServerActive())
		{
			host.Rpc(nameof(GameHost.ClientShowFeedbackText), text, new Godot.Vector3(color.X, color.Y, color.Z));
		}
	}

	public void PingMinimap(int playerIndex, Vector3 position)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		var godotPos = new Godot.Vector3(position.X, position.Y, position.Z);
		if (isLocal)
		{
			host.AddMinimapPing(godotPos);
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientPingMinimap), godotPos);
		}
	}

	public void PlayWarningSound(int playerIndex)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			GameContext.AudioService?.PlayWarningSound();
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientPlayWarningSound));
		}
	}

	public void PlayClickSound(int playerIndex)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			GameContext.AudioService?.PlayClickSound();
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientPlayClickSound));
		}
	}

	public void StartBuildingPlacement(int playerIndex, string unitTypeId)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			host.EnterBuildingPlacement(unitTypeId);
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientStartBuildingPlacement), unitTypeId);
		}
	}

	public void TriggerVictory(int playerIndex)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			if (!host.IsMultiplayerActive())
			{
				BroadcastVictory();
			}
			else
			{
				GameContext.SimulationService?.TriggerVictoryDomain();
				Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, true)).CallDeferred();
			}
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientGameOver), true);
		}
	}

	public void TriggerDefeat(int playerIndex, string reason = "")
	{
		if (!string.IsNullOrEmpty(reason))
		{
			SendMessage(playerIndex, reason);
		}

		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;

		if (isLocal)
		{
			if (!host.IsMultiplayerActive())
			{
				BroadcastDefeat();
			}
			else
			{
				GameContext.SimulationService?.TriggerDefeatDomain();
				Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, false)).CallDeferred();
			}
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientGameOver), false);
		}
	}

	public void BroadcastVictory()
	{
		var host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return;

		GD.Print("[GameHost] Victory triggered by map script!");
		GameContext.SimulationService?.TriggerVictoryDomain();
		Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, true)).CallDeferred();
		if (host.IsMultiplayerServerActive())
		{
			host.Rpc(nameof(GameHost.ClientGameOver), true);
		}
	}

	public void BroadcastDefeat()
	{
		var host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return;

		GD.Print("[GameHost] Defeat triggered by map script!");
		GameContext.SimulationService?.TriggerDefeatDomain();
		Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, false)).CallDeferred();
		if (host.IsMultiplayerServerActive())
		{
			host.Rpc(nameof(GameHost.ClientGameOver), false);
		}
	}

	public void RegisterAbility(int playerIndex, string abilityId, string displayName,
		string tooltip, string iconPath = "", bool isInstant = false)
	{
		if (!TryResolveTargetClient(playerIndex, out var host, out bool isLocal, out int remotePeerId))
			return;
		if (isLocal)
		{
			if (GameContext.RegistryService.RegisterAbility(abilityId, displayName, tooltip, iconPath, isInstant))
				GameContext.InGameHud?.RefreshUI(host.SelectedUnits);
		}
		else if (remotePeerId > 1)
		{
			host.RpcId(remotePeerId, nameof(GameHost.ClientRegisterAbility),
				abilityId, displayName ?? "", tooltip ?? "", iconPath ?? "", isInstant);
		}
	}
}

