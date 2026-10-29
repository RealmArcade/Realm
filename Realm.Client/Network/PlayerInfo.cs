using System;
using System.Text.Json.Serialization;
using Godot;

namespace Realm.Client.Network;

public class PlayerInfo
{
	private string _team = "Team 1";
	private int _teamIndex = 1;

	public int PeerId { get; set; }
	public int Slot { get; set; }
	public string Name { get; set; } = "";
	public string Faction { get; set; } = "HUMAN";
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
	public string Latency { get; set; } = "--";
	public string Jitter { get; set; } = "--";
	public string PacketLoss { get; set; } = "--";
	public bool IsReady { get; set; }
	public string BinaryVersion { get; set; } = "";
	public bool IsMapReady { get; set; } = true;

	/// <summary>Whether this player has finished preparing the match in memory, after holding its map.</summary>
	public bool IsLoadReady { get; set; }

	[JsonIgnore]
	public string SessionToken { get; set; } = Guid.NewGuid().ToString();
	public bool IsDisconnected { get; set; } = false;
}