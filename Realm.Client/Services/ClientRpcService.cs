using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.UI;
using Realm.Ecs;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Services;

/// <summary>
/// Applies a presentation request this player is the audience for: camera movement, camera shake,
/// unit selection, HUD feedback text, minimap pings, audio, building placement mode, and the victory
/// or defeat transition. Every method names the player the request was addressed to, and a request
/// addressed to a player this screen does not control is ignored rather than shown to the wrong
/// person — the authority decides who is told what, and this is where that decision is honoured.
/// </summary>
public class ClientRpcService
{
	private static bool TryResolveLocalPlayer(int playerIndex, out GameHost host)
	{
		host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return false;

		if (playerIndex < 0)
			return false;

		// Never dispatch client UI/camera/selection actions for computer/AI slots.
		if (host.IsPlayerComputer(playerIndex))
			return false;

		return GameContext.PerspectiveFilter.CanControl(playerIndex);
	}

	public void PanCameraTo(int playerIndex, Vector3 position, float duration = 0f)
	{
		if (!TryResolveLocalPlayer(playerIndex, out var host))
			return;

		host.PanCameraInternal(new Godot.Vector3(position.X, position.Y, position.Z), duration);
	}

	public void ShakeCamera(int playerIndex, float intensity, float duration)
	{
		if (!TryResolveLocalPlayer(playerIndex, out var host))
			return;

		host.ShakeCameraInternal(intensity, duration);
	}

	public void BroadcastShakeCamera(float intensity, float duration)
	{
		var host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return;

		host.ShakeCameraInternal(intensity, duration);
	}

	public void SetPlayerCameraBounds(int playerIndex, float minX, float minZ, float maxX, float maxZ)
	{
		var host = GameContext.Host;
		if (host == null)
			return;

		host.RecordPlayerCameraBoundsOverride(playerIndex, minX, minZ, maxX, maxZ);

		if (!TryResolveLocalPlayer(playerIndex, out host))
			return;

		host.SetCameraBoundsInternal(minX, minZ, maxX, maxZ);
	}

	public void ResetPlayerCameraBounds(int playerIndex)
	{
		var host = GameContext.Host;
		if (host == null)
			return;

		host.ClearPlayerCameraBoundsOverride(playerIndex);

		if (!TryResolveLocalPlayer(playerIndex, out host))
			return;

		host.ResetCameraBoundsInternal();
	}

	public void SetPlayerHudElementVisible(int playerIndex, HudElement element, bool visible)
	{
		var host = GameContext.Host;
		if (host == null)
			return;

		host.RecordPlayerHudVisibility(playerIndex, element, visible);

		if (!TryResolveLocalPlayer(playerIndex, out host))
			return;

		host.SetHudElementVisibleInternal(element, visible);
	}

	public void SelectUnit(int playerIndex, UnitId unit)
	{
		if (!unit.IsValid)
			return;

		if (!TryResolveLocalPlayer(playerIndex, out var host))
			return;

		host.SelectUnitInternal(unit);
	}

	public void ClearSelection(int playerIndex)
	{
		if (!TryResolveLocalPlayer(playerIndex, out var host))
			return;

		host.ClearSelectionInternal();
	}

	public void SendMessage(int playerIndex, string message)
	{
		GameLog.LogToConsole($"[MAP MESSAGE P{playerIndex}] {message}");

		ShowFeedbackText(playerIndex, message, new Vector3(0.9f, 0.9f, 0.9f));
	}

	public void BroadcastMessage(string message)
	{
		var host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return;

		GD.Print($"[MAP BROADCAST] {message}");
		BroadcastFeedbackText(message, new Vector3(0.9f, 0.9f, 0.9f));
		GameContext.NetworkService?.BroadcastMessageDomainLogic(message);
	}

	public void ShowFeedbackText(int playerIndex, string text, Vector3 color)
	{
		if (!TryResolveLocalPlayer(playerIndex, out _))
			return;

		if (GameContext.InGameHud == null)
			return;

		var textColor = new Color(color.X, color.Y, color.Z);
		Callable.From(() => GameContext.InGameHud?.ShowFeedbackText(text, textColor)).CallDeferred();
	}

	public void BroadcastFeedbackText(string text, Vector3 color)
	{
		if (GameContext.Host == null || !GodotObject.IsInstanceValid(GameContext.Host))
			return;

		if (GameContext.InGameHud == null)
			return;

		var textColor = new Color(color.X, color.Y, color.Z);
		Callable.From(() => GameContext.InGameHud?.ShowFeedbackText(text, textColor)).CallDeferred();
	}

	public void PingMinimap(int playerIndex, Vector3 position)
	{
		if (!TryResolveLocalPlayer(playerIndex, out var host))
			return;

		host.AddMinimapPing(new Godot.Vector3(position.X, position.Y, position.Z));
	}

	public void PlayWarningSound(int playerIndex)
	{
		if (!TryResolveLocalPlayer(playerIndex, out _))
			return;

		GameContext.AudioService?.PlayWarningSound();
	}

	public void PlayClickSound(int playerIndex)
	{
		if (!TryResolveLocalPlayer(playerIndex, out _))
			return;

		GameContext.AudioService?.PlayClickSound();
	}

	public void StartBuildingPlacement(int playerIndex, string unitTypeId)
	{
		if (!TryResolveLocalPlayer(playerIndex, out var host))
			return;

		host.EnterBuildingPlacement(unitTypeId);
	}

	public void TriggerVictory(int playerIndex)
	{
		if (!TryResolveLocalPlayer(playerIndex, out var host))
			return;

		if (!host.IsMultiplayerActive())
		{
			BroadcastVictory();
			return;
		}

		GameContext.SimulationService?.TriggerVictoryDomain();
		Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, true)).CallDeferred();
	}

	public void TriggerDefeat(int playerIndex, string reason = "")
	{
		if (!string.IsNullOrEmpty(reason))
			SendMessage(playerIndex, reason);

		if (!TryResolveLocalPlayer(playerIndex, out var host))
			return;

		if (!host.IsMultiplayerActive())
		{
			BroadcastDefeat();
			return;
		}

		GameContext.SimulationService?.TriggerDefeatDomain();
		Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, false)).CallDeferred();
	}

	public void BroadcastVictory()
	{
		var host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return;

		GD.Print("[GameHost] Victory triggered by map script!");
		GameContext.SimulationService?.TriggerVictoryDomain();
		Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, true)).CallDeferred();
	}

	public void BroadcastDefeat()
	{
		var host = GameContext.Host;
		if (host == null || !GodotObject.IsInstanceValid(host))
			return;

		GD.Print("[GameHost] Defeat triggered by map script!");
		GameContext.SimulationService?.TriggerDefeatDomain();
		Callable.From(() => GameContext.UserInterface?.TransitionTo(GameScreen.GameOver, false)).CallDeferred();
	}

	public void RegisterAbility(int playerIndex, string abilityId, string displayName,
		string tooltip, string iconPath = "", bool isInstant = false)
	{
		if (!TryResolveLocalPlayer(playerIndex, out var host))
			return;

		if (GameContext.RegistryService.RegisterAbility(abilityId, displayName, tooltip, iconPath, isInstant))
			GameContext.InGameHud?.RefreshUI(host.SelectedUnits);
	}
}
