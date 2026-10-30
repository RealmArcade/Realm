using System.Numerics;
using Realm.Ecs.Components.Core;
using Realm.Shared.Network;
using Realm.Shared.Players;

namespace Realm.ListenServer;

public partial class ServerGameAPI
{
	public Action<int>? PlayerDroppedSink { get; set; }

	public string GetPlayerColorHex(int playerIndex) => PlayerColorPalette.GetColorHex(playerIndex);

	public Vector3 GetPlayerColor(int playerIndex) => PlayerColorPalette.GetColor(playerIndex);

	public string GetPlayerLanguage(int playerIndex) => Localization.GetPlayerLanguage(playerIndex);

	public string Translate(string key, int playerIndex) => Localization.Translate(key, playerIndex);

	public void KickPlayer(int playerIndex, string reason = "Kicked by host")
	{
		if (playerIndex < 0 || playerIndex >= ScriptPlayersState.SlotCount) return;

		string text = string.IsNullOrWhiteSpace(reason) ? "Kicked by host" : reason;
		SetPlayerActive(playerIndex, false);
		_presentation.WriteText(PresentationKind.PlayerKicked, playerIndex, text, Vector3.Zero);
		OnPlayerLeft?.Invoke(playerIndex);
		PlayerDroppedSink?.Invoke(playerIndex);
		_log.Write($"the map dropped player {playerIndex}: {text}");
	}

	public bool AddComputerPlayer(int playerIndex, string? name = null, int teamIndex = -1)
	{
		if (playerIndex <= 0 || playerIndex >= ScriptPlayersState.SlotCount) return false;

		string botName = string.IsNullOrWhiteSpace(name) ? $"Bot {playerIndex}" : name;
		int botTeam = teamIndex >= 0 ? teamIndex : playerIndex;

		MutatePlayersState(playerIndex, (ref ScriptPlayersState playersState, int slot) =>
		{
			playersState.SetName(slot, botName);
			playersState.SetActive(slot, true);
			playersState.SetComputerControlled(slot, true);
		});

		SetPlayerTeam(playerIndex, botTeam);
		_log.Write($"the map seated computer player {playerIndex} as '{botName}' on team {botTeam}");
		return true;
	}

	public void SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
		if (playerIndex < 0 || playerIndex >= ScriptPlayersState.SlotCount) return;

		MutatePlayersState(playerIndex, (ref ScriptPlayersState playersState, int slot) => playersState.SetComputerControlled(slot, isProxy));
		_log.Write($"player {playerIndex} is {(isProxy ? "played by the authority" : "played by its own client")}");
	}
}
