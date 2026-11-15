using System;
using System.Text.Json.Serialization;
using Godot;
using Realm.Shared.Network;

namespace Realm.Client.Network;

public class PlayerInfo
{
	private string _team = "Team 1";
	private int _teamIndex = 1;

	public int PeerId { get; set; }
	public int Slot { get; set; }

	/// <summary>
	///     The account the seat belongs to, or 0 for a guest or computer seat. The name is only ever a display value; this is
	///     the identity.
	/// </summary>
	public long AccountId { get; set; }
	public string Name { get; set; } = "";

	/// <summary>
	///     The SteamID64 this seat's player plays as, or 0 for a computer seat, a guest who is not running inside Steam, or a
	///     player who has not stated one. The seat's own client states it, exactly like the name, and the only thing it is used
	///     for is pointing Steam's add-friend dialog at the person behind the seat.
	/// </summary>
	public ulong SteamId { get; set; }
	public string Faction { get; set; } = string.Empty;
	public int TeamIndex
	{
		get => _teamIndex;
		set
		{
			_teamIndex = value;
			_team = value >= 0 ? $"Team {value}" : (value == -1 ? "Spectator" : "");
		}
	}
	public string Team
	{
		get => _team;
		set
		{
			_team = value ?? "";
			if (value != null && value.StartsWith("Team ") && int.TryParse(value.AsSpan(5), out int parsedTeamIndex))
			{
				_teamIndex = parsedTeamIndex;
			}
			else if (value == "Spectator")
			{
				_teamIndex = -1;
			}
			else
			{
				_teamIndex = 0;
			}
		}
	}
	public Color Color { get; set; } = PlayerColorConfig.GetColor(1);
	public bool IsHost { get; set; }

	public bool IsComputer { get; set; }

	public LobbyBotDifficulty Difficulty { get; set; } = LobbyBotDifficulty.Hard;
	public string Latency { get; set; } = "--";
	public string Jitter { get; set; } = "--";
	public string PacketLoss { get; set; } = "--";

	/// <summary>The round trip the lobby measured between this seat and the host, or -1 while it measured none.</summary>
	public int LatencyMilliseconds { get; set; } = -1;

	/// <summary>The jitter the lobby measured between this seat and the host, or -1 while it measured none.</summary>
	public int JitterMilliseconds { get; set; } = -1;

	/// <summary>The loss the lobby measured between this seat and the host, or -1 while it measured none.</summary>
	public float PacketLossPercent { get; set; } = -1f;

	/// <summary>
	///     The token this seat is measured by, which the lobby mints and publishes. It is not the seat's credential: it
	///     proves nothing and seats nothing, so it only lets the lobby's own players measure a path to each other.
	/// </summary>
	public string ProbeToken { get; set; } = string.Empty;

	/// <summary>The machine stability this seat's own screen published for itself, or empty while it stated none.</summary>
	public string HostStability { get; set; } = string.Empty;
	public bool IsReady { get; set; }
	public string BinaryVersion { get; set; } = "";
	public bool IsMapReady { get; set; } = true;

	public int MapDownloadPercent { get; set; } = 100;

	public bool IsLoadReady { get; set; }

	[JsonIgnore]
	public string SessionToken { get; set; } = Guid.NewGuid().ToString();
	public bool IsDisconnected { get; set; } = false;
}