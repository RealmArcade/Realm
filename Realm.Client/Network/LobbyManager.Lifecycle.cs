using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Client.Utils;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Metadata;
using Realm.Shared.Network;
using Realm.Shared.Services;
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Network;
public partial class LobbyManager
{
	public override void _Process(double delta)
	{
		ClientServerSession? session = _lobbySession;
		if (session == null) return;

		session.Pump();
		TickMatchPreparation();
	}

	private void TickMatchPreparation()
	{
		if (_preparation.HasWork)
		{
			if (!_preparation.Tick()) return;

			GameLog.LogToConsole($"MATCH_LOAD preparation models={_preparation.TotalModels} warm_ms={_preparation.WarmMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}");
			BeginMatchWorldBuild();
			return;
		}

		AdvanceMatchWorldBuild();
	}

	private void BeginMatchWorldBuild()
	{
		if (_worldPrepared && string.Equals(_worldPreparedMapName, ActiveMapName, StringComparison.Ordinal)) return;
		_loadReadyReported = false;
		if (string.IsNullOrWhiteSpace(ActiveMapName)) return;

		GameHost? host = GameContext.Host;
		if (host == null) return;

		if (host.IsBuildingMatchWorld) return;

		if (_worldPrepared) host.DiscardPreparedMatchWorld();
		_worldPrepared = false;
		_worldPreparedMapName = null;

		host.BeginMatchWorldPreparation(ActiveMapName);
		MatchLoadTrace.Mark("world_build_begun");
	}

	private void AdvanceMatchWorldBuild()
	{
		GameHost? host = GameContext.Host;
		if (host == null) return;
		if (!host.IsBuildingMatchWorld)
		{
			ReportWorldPreparedIfReady();
			return;
		}

		if (!host.AdvanceMatchWorldPreparation()) return;

		_worldPrepared = true;
		_worldPreparedMapName = ActiveMapName;
		MatchLoadTrace.Mark("lobby_ready");
		MatchLoadTrace.WriteSummary("lobby_ready");
		ReportWorldPreparedIfReady();
	}

	private void ReportWorldPreparedIfReady()
	{
		if (!_worldPrepared || _loadReadyReported) return;

		PlayerInfo? me = null;
		for (int index = 0; index < PlayerList.Count; index++)
		{
			if (!IsLocalPlayer(PlayerList[index])) continue;
			me = PlayerList[index];
			break;
		}

		if (me == null || !me.IsMapReady) return;

		_loadReadyReported = true;
		ReportLocalLoadReadyState(true);
	}

	public override void _Notification(int what)
	{
		if (what == (int)NotificationWMCloseRequest || what == (int)NotificationPredelete)
		{
			CleanUpLobbySynchronously();
		}
	}

	private void OnProcessExit(object? sender, EventArgs e)
	{
		CleanUpLobbySynchronously();
	}

	private readonly object _cleanupLock = new object();
	private bool _cleanedUp;

	private void CleanUpLobbySynchronously()
	{
		lock (_cleanupLock)
		{
			if (_cleanedUp) return;
			_cleanedUp = true;
		}

		try
		{
			GameContext.PeerSeeder.Stop();
			GameContext.Upnp.ClosePort(BoundPort);
			GameContext.LocalAuthority.Dispose();
			_lobbySession?.Dispose();
		}
		catch
		{
		}

		if (IsHost && !string.IsNullOrEmpty(ActiveLobbyId) && !string.IsNullOrEmpty(_hostToken))
		{
			string lobbyIdToClose = ActiveLobbyId;
			string tokenToClose = _hostToken;
			_hostToken = null;

			try
			{
				GameLog.LogToConsole($"[LobbyManager] Closing lobby {lobbyIdToClose} synchronously before exit...");
				var payload = new { LobbyId = lobbyIdToClose, HostToken = tokenToClose };
				var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

				var task = Task.Run(async () => { await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/close", jsonContent); });
				task.Wait(2000);
			}
			catch (Exception ex)
			{
				GameLog.LogError($"[LobbyManager] Failed synchronous lobby close on exit: {ex.Message}");
			}
		}
	}

}
