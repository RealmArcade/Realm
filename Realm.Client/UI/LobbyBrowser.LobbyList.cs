// The lobby list end to end: fetch the registry listing, parse each entry into LobbyData (including the
// ping estimate and map size), apply the tag and search filters, then render one row per surviving lobby.
// Every path in here is about a list the user has not touched yet, which is why it is separate from the
// wiring in the root file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using HttpClient = System.Net.Http.HttpClient;
using Timer = Godot.Timer;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Utils;
using Realm.Client.Services;


namespace Realm.Client.UI;

public partial class LobbyBrowser
{
	private void FetchLobbiesFromRegistry()
	{
		if (GameContext.Lobby is not { } lobby) return;

		lobby.RandomizeServerIndex();
		Task.Run(async () =>
		{
			try
			{
				var json = await GameContext.Lobby.FetchLobbiesRawAsync();
				if (json == null)
				{
					Callable.From(() => OnLobbiesFetchedFallback()).CallDeferred();
					return;
				}

				int clientBaseline = await GameContext.Lobby.MeasurePingToRegistryAsync();
				using var doc = JsonDocument.Parse(json);
				var lobbyList = new List<LobbyData>();

				foreach (var item in doc.RootElement.EnumerateArray())
				{
					lobbyList.Add(ParseLobbyData(item, clientBaseline));
				}

				Callable.From(() => OnLobbiesFetched(lobbyList)).CallDeferred();
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[LobbyBrowser] Error fetching lobbies: {ex.Message}");
				Callable.From(() => OnLobbiesFetchedFallback()).CallDeferred();
			}
		});
	}

	private static string GetStringProp(JsonElement item, string key, string fallback = "")
	{
		if (item.TryGetProperty(key, out var prop))
		{
			return prop.GetString() ?? fallback;
		}

		return fallback;
	}

	private static int GetIntProp(JsonElement item, string key, int fallback = 0)
	{
		if (item.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number)
		{
			return prop.GetInt32();
		}

		return fallback;
	}

	private static bool GetBoolProp(JsonElement item, string key, bool fallback = false)
	{
		if (item.TryGetProperty(key, out var prop) && prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
		{
			return prop.GetBoolean();
		}

		return fallback;
	}

	private static LobbyData ParseLobbyData(JsonElement item, int clientBaseline)
	{
		string hostIp = GetStringProp(item, "hostIP");
		if (string.IsNullOrEmpty(hostIp))
		{
			hostIp = GetStringProp(item, "hostIp");
		}

		int calculatedPing = CalculatePing(item, hostIp, clientBaseline);
		long mapSizeBytes = GetMapSizeBytes(item);

		string mapName = GetStringProp(item, "map");
		string mapVersion = GetStringProp(item, "mapVersion");

		if (mapSizeBytes <= 0)
		{
			mapSizeBytes = MapAssetManager.GetMapTotalSizeBytes(mapName, mapVersion);
		}

		int slotsUsed = GetIntProp(item, "slotsUsed", 0);
		int maxPlayers = GetIntProp(item, "maxPlayers", 8);

		return new LobbyData
		{
			LobbyId = GetStringProp(item, "lobbyId"),
			Map = mapName,
			Mode = "Melee",
			IsGreenlit = GetBoolProp(item, "isGreenlit"),
			MapCreator = GetStringProp(item, "mapCreator"),
			Players = $"{slotsUsed}/{maxPlayers}",
			Ping = calculatedPing,
			GameVersion = GetStringProp(item, "gameVersion"),
			MapSizeBytes = mapSizeBytes,
			MapSizeFormatted = MapAssetManager.FormatSizeInMB(mapSizeBytes)
		};
	}

	private static int CalculatePing(JsonElement item, string hostIp, int clientBaseline)
	{
		if (hostIp == "127.0.0.1" || hostIp == "localhost" || string.IsNullOrEmpty(hostIp))
		{
			return 5;
		}

		double distanceKm = 0;
		if (item.TryGetProperty("distanceKm", out var distProp))
		{
			distanceKm = distProp.GetDouble();
		}
		else if (item.TryGetProperty("distance", out distProp))
		{
			distanceKm = distProp.GetDouble();
		}

		int hostPingBaseline = 20;
		if (item.TryGetProperty("hostPingBaseline", out var baselineProp))
		{
			hostPingBaseline = baselineProp.GetInt32();
		}

		double geoPing = 10.0 + (distanceKm / 100.0);
		double clientOverhead = Math.Max(0, clientBaseline - 20);
		double hostOverhead = Math.Max(0, hostPingBaseline - 20);
		return (int)Math.Round(geoPing + clientOverhead + hostOverhead);
	}

	private static long GetMapSizeBytes(JsonElement item)
	{
		if (item.TryGetProperty("mapSizeBytes", out var sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
		{
			return sizeProp.GetInt64();
		}

		if (item.TryGetProperty("MapSizeBytes", out sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
		{
			return sizeProp.GetInt64();
		}

		if (item.TryGetProperty("mapSize", out sizeProp) && sizeProp.ValueKind == JsonValueKind.Number)
		{
			return sizeProp.GetInt64();
		}

		return 0;
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

	private void OnLobbiesFetchedFallback()
	{
		_connectionFailed = true;
		_allLobbies.Clear();
		ApplyFilters();
	}

	private string GetMapDisplayName(LobbyData data)
	{
		if (data.IsGreenlit) return data.Map;

		string betaDisplayName = $"[{Tr("Beta-Testing")}] {data.Map}";
		return string.IsNullOrEmpty(data.MapCreator) ? betaDisplayName : $"{betaDisplayName} - {data.MapCreator}";
	}

	private void ApplyFilters()
	{
		_filteredLobbies = _allLobbies.ToList();

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

		if (!_betaTestingMapsCheck.ButtonPressed)
		{
			_filteredLobbies = _filteredLobbies.Where(x => x.IsGreenlit).ToList();
		}

		if (!_versionMismatchCheck.ButtonPressed)
		{
			_filteredLobbies = _filteredLobbies.Where(x => IsJoinableGameVersion(x.GameVersion)).ToList();
		}

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
			lbl.Name = "RegistryUnavailable_Label";
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

		bool isBetaTesting = !data.IsGreenlit;

		var lblMap = new Label();
		lblMap.Name = "LblMap_Label";
		lblMap.Text = $"  {GetMapDisplayName(data)}";
		lblMap.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblMap.AddThemeColorOverride("font_color", isBetaTesting ? new Color(0.95f, 0.6f, 0.35f) : new Color(0.9f, 0.85f, 0.7f));
		lblMap.AddThemeFontSizeOverride("font_size", 16);
		lblMap.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblMap);

		var lblSize = new Label();
		lblSize.Name = "LblSize_Label";
		lblSize.Text = data.MapSizeFormatted;
		lblSize.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblSize.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.8f));
		lblSize.AddThemeFontSizeOverride("font_size", 15);
		lblSize.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblSize);

		var lblMode = new Label();
		lblMode.Name = "LblMode_Label";
		lblMode.Text = data.Mode;
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

		var lblPing = new Label();
		lblPing.Name = "LblPing_Label";
		lblPing.Text = $"{data.Ping} ms  ";
		lblPing.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblPing.HorizontalAlignment = HorizontalAlignment.Right;
		lblPing.AddThemeColorOverride("font_color", data.Ping < 80 ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.85f, 0.65f, 0.3f));
		lblPing.AddThemeFontSizeOverride("font_size", 15);
		lblPing.VerticalAlignment = VerticalAlignment.Center;
		hBox.AddChild(lblPing);

		var lblVersion = new Label();
		lblVersion.Name = "LblVersion_Label";
		lblVersion.Text = $"v{data.GameVersion}  ";
		lblVersion.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblVersion.HorizontalAlignment = HorizontalAlignment.Right;
		lblVersion.AddThemeColorOverride("font_color", data.GameVersion == RealmVersion.GameBinaryVersion ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.85f, 0.3f, 0.3f));
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
					ShowVersionMismatchPopup(data.GameVersion, RealmVersion.GameBinaryVersion);
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
						: "Direct connection handshake failed. The host could not be reached.";
					GameContext.Lobby.LobbyJoinError = null;
					ShowLobbyJoinErrorPopup(errMsg);
				}
			}
		};

		return panel;
	}

}