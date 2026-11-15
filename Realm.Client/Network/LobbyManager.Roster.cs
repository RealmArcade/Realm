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
	private List<MatchRosterSeat> BuildMatchRoster()
	{
		var seats = new List<MatchRosterSeat>(PlayerList.Count);
		for (int index = 0; index < PlayerList.Count; index++)
		{
			PlayerInfo player = PlayerList[index];
			if (player.Slot <= 0) continue;

			bool isComputer = string.Equals(player.Faction, "AI", StringComparison.OrdinalIgnoreCase) || player.PeerId < 0;
			int difficulty = isComputer
				? (int)LobbyBotDifficultyRules.Clamp((int)player.Difficulty)
				: (int)LobbyBotDifficultyRules.Default;
			seats.Add(new MatchRosterSeat(player.Slot, player.AccountId, player.Name, ResolveSeatTeam(player.Team, player.Slot), player.Team, isComputer, difficulty, player.Faction));
		}

		seats.Sort((left, right) => left.Slot.CompareTo(right.Slot));
		return seats;
	}

	private static int ResolveSeatTeam(string? teamName, int slot)
	{
		if (string.Equals(teamName, LobbyLimits.SpectatorTeamName, StringComparison.Ordinal)) return 0;

		if (!string.IsNullOrWhiteSpace(teamName)
			&& teamName.StartsWith("Team ", StringComparison.OrdinalIgnoreCase)
			&& int.TryParse(teamName.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int team)
			&& team > 0)
		{
			return team;
		}

		return slot;
	}

	public async Task<int> MeasurePingToRegistryAsync()
	{
		var stopwatch = Stopwatch.StartNew();
		try
		{
			var response = await _httpClient.GetAsync($"{RegistryServerUrl}/lobbies");
			stopwatch.Stop();
			if (response.IsSuccessStatusCode)
			{
				return (int)stopwatch.ElapsedMilliseconds;
			}
		}
		catch
		{
		}

		return 100;
	}

	public override void _ExitTree()
	{
		GameContext.Account.Changed -= OnAccountChanged;
		GameContext.UnregisterLobby(this);
	}

	public static string GetLocalIPAddress()
	{
		try
		{
			using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
			{
				socket.Connect("8.8.8.8", 65530);
				var endPoint = socket.LocalEndPoint as IPEndPoint;
				if (endPoint != null)
				{
					return endPoint.Address.ToString();
				}
			}
		}
		catch
		{
			try
			{
				var host = Dns.GetHostEntry(Dns.GetHostName());
				foreach (var ip in host.AddressList)
				{
					if (ip.AddressFamily == AddressFamily.InterNetwork)
					{
						return ip.ToString();
					}
				}
			}
			catch
			{
			}
		}

		return "127.0.0.1";
	}

	private static bool IsPrivateIp(string ip)
	{
		if (ip == "127.0.0.1") return true;
		if (ip == "localhost") return true;
		if (!IPAddress.TryParse(ip, out var address)) return false;

		byte[] bytes = address.GetAddressBytes();
		if (bytes.Length != 4) return false;

		if (bytes[0] == 10) return true;
		if (bytes[0] == 192 && bytes[1] == 168) return true;
		if (bytes[0] == 172 && IsInRange(bytes[1], 16, 31)) return true;
		if (bytes[0] == 100 && IsInRange(bytes[1], 64, 127)) return true;
		if (bytes[0] == 26) return true;

		return false;
	}

	private static bool IsInRange(byte value, byte min, byte max)
	{
		return value >= min && value <= max;
	}

}
