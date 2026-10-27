using System.Numerics;
using Realm.Ecs.Components.Core;
using Realm.Shared.Network;
using Realm.Shared.Players;

namespace Realm.ListenServer;

/// <summary>
///     Who is in the match, what each of them looks like, and what language each of them reads. A map decides who is
///     playing, so the authority is where a computer player is seated, where a player is dropped from the match, and
///     where a script's question about a player's color or language is answered.
/// </summary>
public partial class ServerGameAPI
{
	/// <summary>
	///     What an authority does when a map drops a player: the connection that player is playing through is ended.
	///     The surface has no transports of its own, so the host that owns them is handed the player to end.
	/// </summary>
	public Action<int>? PlayerDroppedSink { get; set; }

	/// <summary>The color a player is drawn in, as six hexadecimal digits.</summary>
	public string GetPlayerColorHex(int playerIndex) => PlayerColorPalette.GetColorHex(playerIndex);

	/// <summary>The color a player is drawn in, as red, green and blue fractions.</summary>
	public Vector3 GetPlayerColor(int playerIndex) => PlayerColorPalette.GetColor(playerIndex);

	/// <summary>The language a player reads in, as the client that player is playing through reported it.</summary>
	public string GetPlayerLanguage(int playerIndex) => Localization.GetPlayerLanguage(playerIndex);

	/// <summary>
	///     What one of the map's own keys reads as for one player. The text comes from the map's own language files and
	///     from the language that player connected in, so a map that shows a line to a player shows it in that player's
	///     language rather than in the authority's.
	/// </summary>
	public string Translate(string key, int playerIndex) => Localization.Translate(key, playerIndex);

	/// <summary>
	///     Drops a player from the match: the slot stops being active, the map is told, and the connection that player is
	///     playing through is ended so the match it is watching does not continue on a screen that is out of the game.
	/// </summary>
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

	/// <summary>
	///     Seats a computer player in a slot, which is what a map does when it plays a slot itself or when it fills one
	///     with its own opponent. The slot is named, marked as computer controlled and put on a team, and the bot that
	///     plays it is the one the map configured for that slot.
	/// </summary>
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

	/// <summary>
	///     Marks a player as played by the authority rather than by a person, which is what a map does when it takes a
	///     slot over without a computer opponent taking it.
	/// </summary>
	public void SetPlayerComputerControlled(int playerIndex, bool isProxy)
	{
		if (playerIndex < 0 || playerIndex >= ScriptPlayersState.SlotCount) return;

		MutatePlayersState(playerIndex, (ref ScriptPlayersState playersState, int slot) => playersState.SetComputerControlled(slot, isProxy));
		_log.Write($"player {playerIndex} is {(isProxy ? "played by the authority" : "played by its own client")}");
	}
}
