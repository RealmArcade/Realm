// The lobby list end to end: ask Steam for the lobbies this screen could join, read each one's advertisement
// into LobbyData, apply the tag and search filters, then render one row per surviving lobby. Every path in
// here is about a list the user has not touched yet, which is why it is separate from the wiring in the
// root file.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using Steamworks;
using Timer = Godot.Timer;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;


namespace Realm.Client.UI;

public partial class LobbyBrowser
{
	/// <summary>
	///     Asks Steam for the lobbies this screen could join and reads each one's advertisement. Steam is the directory, so
	///     there is no server to poll, no region node to pick and nothing measured about anybody's network: a lobby is either
	///     in Steam's answer or it does not exist.
	/// </summary>
	private void FetchLobbiesFromRegistry()
	{
		_ = FetchLobbiesFromSteamAsync();
	}

	private async Task FetchLobbiesFromSteamAsync()
	{
		if (!SteamLobbyDirectory.IsAvailable)
		{
			OnLobbiesUnavailable();
			return;
		}

		try
		{
			IReadOnlyList<ulong> lobbyIds = await SteamLobbyDirectory.BrowseAsync();
			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;

			var lobbyList = new List<LobbyData>(lobbyIds.Count);
			foreach (ulong lobbyId in lobbyIds) lobbyList.Add(ReadLobby(lobbyId));

			OnLobbiesFetched(lobbyList);
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[LobbyBrowser] Steam's lobby list could not be read: {exception.Message}");
			if (GodotObject.IsInstanceValid(this) && IsInsideTree()) OnLobbiesUnavailable();
		}
	}

	/// <summary>
	///     Reads one Steam lobby's advertisement into the row the browser draws. The map's compatibility is decided here so a
	///     lobby whose generation or build this screen cannot play is marked rather than joined and then failed.
	/// </summary>
	/// <param name="lobbyId">The Steam lobby id.</param>
	/// <returns>The lobby as the browser shows it.</returns>
	private static LobbyData ReadLobby(ulong lobbyId)
	{
		string map = SteamLobbyDirectory.GetData(lobbyId, SteamLobbyDirectory.MapKey);
		string mapVersion = SteamLobbyDirectory.GetData(lobbyId, SteamLobbyDirectory.MapVersionKey);
		int seats = ParseSeatCount(SteamLobbyDirectory.GetData(lobbyId, SteamLobbyDirectory.SeatsKey));
		int members = SteamLobbyDirectory.GetMemberCount(lobbyId);
		ulong owner = SteamLobbyDirectory.GetOwnerSteamId(lobbyId);

		(string reason, bool compatible) = DescribeCompatibility(map, SteamLobbyDirectory.GetData(lobbyId, SteamLobbyDirectory.EngineGenerationKey));

		return new LobbyData
		{
			LobbyId = lobbyId.ToString(),
			Map = string.IsNullOrEmpty(map) ? "melee" : map,
			MapVersion = string.IsNullOrEmpty(mapVersion) ? "1.0.0" : mapVersion,
			Mode = SteamLobbyDirectory.GetData(lobbyId, SteamLobbyDirectory.ModeKey),
			HostName = DescribeHost(owner),
			HostSteamId = SteamLobbyDirectory.GetData(lobbyId, SteamLobbyDirectory.HostSteamIdKey),
			Players = $"{members}/{seats}",
			GameVersion = SteamLobbyDirectory.GetData(lobbyId, SteamLobbyDirectory.GameBuildKey),
			RequiresCode = SteamLobbyDirectory.GetData(lobbyId, SteamLobbyDirectory.RequiresCodeKey) == "1",
			IsCompatible = compatible,
			IncompatibleReason = reason
		};
	}

	private static int ParseSeatCount(string seats)
	{
		return int.TryParse(seats, out int parsed) && parsed > 0 ? parsed : 8;
	}

	private static string DescribeHost(ulong ownerSteamId)
	{
		if (ownerSteamId == 0) return string.Empty;

		try
		{
			return SteamFriends.GetFriendPersonaName(new CSteamID(ownerSteamId));
		}
		catch (Exception)
		{
			return string.Empty;
		}
	}

	/// <summary>
	///     Decides whether this screen can play a lobby's map. The build is filtered by Steam before the list arrives, so
	///     what is left to check is the generation the map declares: one newer than this game cannot be read, and one the game
	///     still supports is upgraded on a private copy.
	/// </summary>
	/// <param name="map">The lobby's map path name.</param>
	/// <param name="generationText">The engine generation the map declares.</param>
	/// <returns>Why the lobby cannot be played, and whether it can.</returns>
	private static (string Reason, bool Compatible) DescribeCompatibility(string map, string generationText)
	{
		if (string.IsNullOrEmpty(map)) return (string.Empty, true);
		if (!int.TryParse(generationText, out int generation)) return (string.Empty, true);
		if (generation <= RealmVersion.EngineGeneration) return (string.Empty, true);

		return (TranslationServer.Translate("This map was made for a newer version of Realm."), false);
	}


	private void OnLobbiesFetched(List<LobbyData> list)
	{
		_connectionFailed = false;
		_allLobbies.Clear();
		if (list != null)
		{
			_allLobbies.AddRange(list);
		}

		ApplyFilters();
	}

	/// <summary>
	///     States that Steam could not answer, which is the only way the list is unavailable now that Steam is the directory.
	///     There is no second server to fall back to and no address to type instead.
	/// </summary>
	private void OnLobbiesUnavailable()
	{
		_connectionFailed = true;
		_allLobbies.Clear();
		ApplyFilters();
	}

	private string GetMapDisplayName(LobbyData data)
	{
		if (string.IsNullOrEmpty(data.HostName)) return data.Map;

		return $"{data.Map} - {data.HostName}";
	}

	private void ApplyFilters()
	{
		// Nothing is filtered out by default. An empty browser with no explanation is a worse failure than one weak lobby, and
		// Steam has already narrowed the list to lobbies this build can talk to.
		_filteredLobbies = _allLobbies.ToList();
		_hiddenUnreachableCount = 0;

		string query = _searchBar.Text.Trim().ToLower();
		if (!string.IsNullOrEmpty(query))
		{
			_filteredLobbies = _filteredLobbies.Where(x => x.Map.ToLower().Contains(query)).ToList();
		}

		var activeModes = GetActiveModes();
		if (activeModes.Count > 0)
		{
			_filteredLobbies = _filteredLobbies.Where(x => activeModes.Contains(x.Mode.ToLower())).ToList();
		}

		if (!_versionMismatchCheck.ButtonPressed)
		{
			_filteredLobbies = _filteredLobbies.Where(x => IsJoinableGameVersion(x.GameVersion)).ToList();
		}

		// Sorted by map, because Steam supplies no distance or ping for a lobby and inventing one would be a column that looks
		// measured and is not.
		_filteredLobbies.Sort((left, right) => string.Compare(left.Map, right.Map, StringComparison.OrdinalIgnoreCase));

		RefreshLobbyDisplay();
	}

	private static bool IsJoinableGameVersion(string lobbyGameVersion)
	{
		if (string.IsNullOrEmpty(lobbyGameVersion)) return true;

		return string.Equals(lobbyGameVersion, RealmVersion.GameBinaryVersion, StringComparison.Ordinal);
	}
	private HashSet<string> GetActiveModes()
	{
		var modes = new HashSet<string>();
		foreach (var (tag, checkBox) in _tagFilters)
		{
			if (checkBox.ButtonPressed) modes.Add(tag.ToLowerInvariant());
		}

		return modes;
	}

	private void RefreshLobbyDisplay()
	{
		foreach (Node child in _lobbyListContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (_connectionFailed)
		{
			var lbl = new Label();
			lbl.Name = "SteamUnavailable_Label";
			lbl.Text = TranslationServer.Translate("⚠️ Failed to connect to the lobby registry server. Please check your internet connection or try again later.\n\nSingle player against AI bots is always available from the main menu.");
			lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			lbl.CustomMinimumSize = new Vector2(760, 0);
			lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			lbl.HorizontalAlignment = HorizontalAlignment.Center;
			lbl.VerticalAlignment = VerticalAlignment.Center;
			lbl.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
			lbl.AddThemeFontSizeOverride("font_size", 15);
			_lobbyListContainer.AddChild(lbl);
		}
		else if (_filteredLobbies.Count == 0)
		{
			bool noLobbiesOnline = _allLobbies.Count == 0;
			var lbl = new Label();
			lbl.Name = noLobbiesOnline ? "NoLobbiesOnline_Label" : "NoLobbiesMatched_Label";
			lbl.Text = noLobbiesOnline
				? TranslationServer.Translate("No lobbies are open right now. Realm is new, so the player base is still growing.\n\nStart a lobby and invite friends, or play single player against AI bots.")
				: TranslationServer.Translate("No lobbies match your search or filters.");
			lbl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			lbl.CustomMinimumSize = new Vector2(760, 0);
			lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			lbl.HorizontalAlignment = HorizontalAlignment.Center;
			lbl.VerticalAlignment = VerticalAlignment.Center;
			lbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
			lbl.AddThemeFontSizeOverride("font_size", 15);

			if (noLobbiesOnline)
			{
				var spacer = new Control();
				spacer.Name = "NoLobbiesOnline_Spacer";
				spacer.CustomMinimumSize = new Vector2(0, 50);
				_lobbyListContainer.AddChild(spacer);
			}

			_lobbyListContainer.AddChild(lbl);
		}
		else
		{
			foreach (var lobby in _filteredLobbies)
			{
				var row = CreateLobbyRow(lobby);
				_lobbyListContainer.AddChild(row);
			}
		}
	}

	private PanelContainer CreateLobbyRow(LobbyData data)
	{
		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.CustomMinimumSize = new Vector2(0, 48);

		var rowStyle = UIStyle.CreateLobbyBrowserRowPlate(false);
		var hoverStyle = UIStyle.CreateLobbyBrowserRowPlate(true);

		panel.AddThemeStyleboxOverride("panel", rowStyle);

		var hBox = new HBoxContainer();
		hBox.Name = "HBox_HBoxContainer";
		panel.AddChild(hBox);

		var lblMap = new Label();
		lblMap.Name = "LblMap_Label";
		lblMap.Text = $"  {GetMapDisplayName(data)}";
		lblMap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblMap.AddThemeColorOverride("font_color", data.IsCompatible ? new Color(0.9f, 0.85f, 0.7f) : new Color(0.85f, 0.45f, 0.35f));
		lblMap.AddThemeFontSizeOverride("font_size", 16);
		lblMap.VerticalAlignment = VerticalAlignment.Center;
		if (!data.IsCompatible) lblMap.TooltipText = data.IncompatibleReason;
		hBox.AddChild(lblMap);

		// A lobby gated by a join code says so, because the code is what admits a seat and the lobby is still listed.
		if (data.RequiresCode)
		{
			var lblCode = new Label();
			lblCode.Name = "LblJoinCode_Label";
			lblCode.Text = TranslationServer.Translate("CODE");
			lblCode.TooltipText = TranslationServer.Translate("This lobby asks for a join code before it seats you.");
			lblCode.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			lblCode.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.4f));
			lblCode.AddThemeFontSizeOverride("font_size", 14);
			lblCode.VerticalAlignment = VerticalAlignment.Center;
			hBox.AddChild(lblCode);
		}

		var lblMode = new Label();
		lblMode.Name = "LblMode_Label";
		lblMode.Text = string.IsNullOrEmpty(data.Mode) ? "Melee" : data.Mode;
		lblMode.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblMode.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		lblMode.AddThemeFontSizeOverride("font_size", 15);
		lblMode.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblMode);

		var lblPlayers = new Label();
		lblPlayers.Name = "LblPlayers_Label";
		lblPlayers.Text = data.Players;
		lblPlayers.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblPlayers.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		lblPlayers.AddThemeFontSizeOverride("font_size", 15);
		lblPlayers.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblPlayers);

		var lblVersion = new Label();
		lblVersion.Name = "LblVersion_Label";
		lblVersion.Text = $"{data.GameVersion}  ";
		lblVersion.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblVersion.HorizontalAlignment = HorizontalAlignment.Right;
		lblVersion.AddThemeColorOverride("font_color", new Color(0.3f, 0.8f, 0.4f));
		lblVersion.AddThemeFontSizeOverride("font_size", 15);
		lblVersion.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblVersion);

		panel.MouseEntered += () =>
		{
			GameContext.UserInterface?.PlayHoverSound();
			panel.AddThemeStyleboxOverride("panel", hoverStyle);
		};
		panel.MouseExited += () => panel.AddThemeStyleboxOverride("panel", rowStyle);

		panel.GuiInput += async (@event) =>
		{
			if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed && mouseBtn.ButtonIndex == MouseButton.Left)
			{
				GameContext.UserInterface?.PlayClickSound();

				if (!string.IsNullOrEmpty(data.GameVersion) && data.GameVersion != RealmVersion.GameBinaryVersion)
				{
					ShowDifferentBuildNotice(data.GameVersion, RealmVersion.GameBinaryVersion);
					return;
				}

				// A map made for a newer engine is refused before the join instead of opened and then failed, so the player reads
				// which of the two is out of date.
				if (!data.IsCompatible)
				{
					GameContext.UserInterface?.PlayWarningSound();
					GameContext.UserInterface?.ShowPopupMessage(TranslationServer.Translate("MAP NOT COMPATIBLE"), data.IncompatibleReason);
					return;
				}

				panel.MouseFilter = MouseFilterEnum.Ignore;
				var connectingOverlay = ShowConnectingPopup();

				if (GameContext.Lobby is not { } lobby) return;

				lobby.ActiveMapName = data.Map;
				bool success = await lobby.JoinLobbyAsync(data.LobbyId);

				if (connectingOverlay != null && GodotObject.IsInstanceValid(connectingOverlay))
				{
					connectingOverlay.QueueFree();
				}

				if (success)
				{
					GameContext.UserInterface?.TransitionTo(GameScreen.LobbyRoom);
				}
				else
				{
					GameContext.UserInterface?.PlayWarningSound();
					panel.MouseFilter = MouseFilterEnum.Stop;
					string errMsg = !string.IsNullOrEmpty(GameContext.Lobby.LobbyJoinError)
						? GameContext.Lobby.LobbyJoinError
						: TranslationServer.Translate("Steam could not connect to the host.");
					GameContext.Lobby.LobbyJoinError = null;
					ShowLobbyJoinErrorPopup(errMsg);
				}
			}
		};

		return panel;
	}

}