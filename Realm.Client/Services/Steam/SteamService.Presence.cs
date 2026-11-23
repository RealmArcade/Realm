using System;
using Godot;
using Realm.Client.Network;
using Realm.Client.UI;
using Steamworks;

namespace Realm.Client.Services;

/// <summary>What this screen is doing, as Steam shows it to friends.</summary>
public enum SteamPresenceState
{
	Menus,
	Lobby,
	Match
}

/// <summary>
///     The player's Steam status and the Join Game invitation behind it. Steam is the social surface: a friend reads the
///     state here, right-clicks the player and joins the lobby from Steam's own menu, and the game only has to say where
///     the player is. Nothing about the network is stated - no address, port or machine name - because Steam carries the
///     connection and the lobby id is the only thing a join needs.
/// </summary>
public partial class SteamService
{
	/// <summary>
	///     The rich-presence token Steam's app configuration localises. Each value is a token the Steamworks partner site
	///     turns into a sentence in the player's own language, which is why no English text is written from here.
	/// </summary>
	private const string DisplayTokenKey = "steam_display";

	/// <summary>The token for standing in the menus.</summary>
	private const string MenuDisplayToken = "#Realm_Menu";

	/// <summary>The token for standing in a lobby, which reads the lobby size and map from the keys below.</summary>
	private const string LobbyDisplayToken = "#Realm_Lobby";

	/// <summary>The token for playing a match.</summary>
	private const string MatchDisplayToken = "#Realm_Match";

	private const string StatusKey = "status";
	private const string MapKey = "map";
	private const string ModeKey = "mode";
	private const string PlayersKey = "players";
	private const string SeatsKey = "seats";

	/// <summary>The key Steam reads to offer a friend the Join Game action.</summary>
	private const string ConnectKey = "connect";

	/// <summary>What Steam is currently being told, so a frame that changes nothing writes nothing.</summary>
	private string _appliedPresence = string.Empty;

	/// <summary>
	///     Whether Steam's overlay is enabled for this game. An overlay that is disabled is not a failure and does not block
	///     anything: the invitations and the friends list simply open in Steam's own window, which the player still has.
	/// </summary>
	public bool IsOverlayEnabled
	{
		get
		{
			if (!_initialized) return false;

			try
			{
				return SteamUtils.IsOverlayEnabled();
			}
			catch (Exception exception)
			{
				GameLog.LogError($"[Steam] the overlay state could not be read: {exception.Message}");
				return false;
			}
		}
	}

	/// <summary>
	///     Writes the player's Steam status and, while they are in a lobby, the connect string a friend's Join Game action
	///     uses. It is called once per frame and writes only when the state it would state has changed, because Steam's
	///     presence is a string comparison on its side and a per-frame write would be a per-frame round trip for nothing.
	/// </summary>
	public void RefreshPresence()
	{
		if (!_initialized) return;

		LobbyManager? lobby = GameContext.Lobby;
		UIManager? userInterface = GameContext.UserInterface;

		SteamPresenceState state = SteamPresenceState.Menus;
		string mapName = string.Empty;
		string mode = string.Empty;
		int members = 0;
		int seats = 0;
		ulong lobbyId = 0;

		bool inLobby = lobby != null && GodotObject.IsInstanceValid(lobby) && lobby.HasLobbyState;
		if (inLobby)
		{
			state = SteamPresenceState.Lobby;
			mapName = lobby!.ActiveMapName ?? string.Empty;
			mode = "Melee";
			members = lobby.PlayingPlayerCount;
			seats = lobby.MaxPlayers;
			lobbyId = lobby.SteamLobbyId;
		}

		if (userInterface != null && GodotObject.IsInstanceValid(userInterface) && userInterface.CurrentScreen is GameScreen.InGameHUD or GameScreen.GameOver)
		{
			state = SteamPresenceState.Match;
		}

		string signature = $"{state}|{mapName}|{mode}|{members}|{seats}|{lobbyId}";
		if (string.Equals(signature, _appliedPresence, StringComparison.Ordinal)) return;

		_appliedPresence = signature;

		try
		{
			switch (state)
			{
				case SteamPresenceState.Match:
					SteamFriends.SetRichPresence(DisplayTokenKey, MatchDisplayToken);
					SteamFriends.SetRichPresence(StatusKey, TranslationServer.Translate("In Match"));
					SteamFriends.SetRichPresence(MapKey, mapName);
					SteamFriends.SetRichPresence(ModeKey, mode);
					ClearConnectString();
					break;

				case SteamPresenceState.Lobby:
					SteamFriends.SetRichPresence(DisplayTokenKey, LobbyDisplayToken);
					SteamFriends.SetRichPresence(StatusKey, TranslationServer.Translate("In Lobby"));
					SteamFriends.SetRichPresence(MapKey, mapName);
					SteamFriends.SetRichPresence(PlayersKey, members.ToString(System.Globalization.CultureInfo.InvariantCulture));
					SteamFriends.SetRichPresence(SeatsKey, seats.ToString(System.Globalization.CultureInfo.InvariantCulture));
					SetConnectString(lobbyId);
					break;

				default:
					SteamFriends.SetRichPresence(DisplayTokenKey, MenuDisplayToken);
					SteamFriends.SetRichPresence(StatusKey, TranslationServer.Translate("In the menus"));
					ClearConnectString();
					break;
			}
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the player's status could not be written: {exception.Message}");
		}
	}

	/// <summary>
	///     Clears the status this process wrote, which is what the game asks for on the way out. A status left behind would
	///     tell a friend the player is still in a lobby that no longer exists.
	/// </summary>
	public void ClearPresence()
	{
		if (!_initialized) return;

		_appliedPresence = string.Empty;

		try
		{
			SteamFriends.ClearRichPresence();
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[Steam] the player's status could not be cleared: {exception.Message}");
		}
	}

	/// <summary>
	///     States the lobby a friend's Join Game action should enter, in the launch arguments Steam hands the game. The
	///     argument is Steam's own <c>+connect_lobby</c> form, so a friend who chooses Join Game starts this game already
	///     holding the lobby id rather than needing an address or a code.
	/// </summary>
	/// <param name="lobbyId">The Steam lobby id, or zero to clear the invitation.</param>
	private void SetConnectString(ulong lobbyId)
	{
		if (lobbyId == 0)
		{
			ClearConnectString();
			return;
		}

		SteamFriends.SetRichPresence(ConnectKey, $"+connect_lobby {lobbyId}");
	}

	private void ClearConnectString()
	{
		SteamFriends.SetRichPresence(ConnectKey, null);
	}
}
