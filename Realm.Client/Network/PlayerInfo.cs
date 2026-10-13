using System;
using Godot;

namespace Realm.Client.Network;

public class PlayerInfo
{
	public int PeerId { get; set; }
	public int Slot { get; set; }
	public string Name { get; set; } = "";
	public string Faction { get; set; } = "HUMAN";
	public string Team { get; set; } = "Team 1";
	public Color Color { get; set; } = PlayerColorConfig.GetColor(1);
	public bool IsHost { get; set; }
	public string Latency { get; set; } = "--";
	public string Jitter { get; set; } = "--";
	public string PacketLoss { get; set; } = "--";
	public bool IsReady { get; set; }
	public string BinaryVersion { get; set; } = "";
	public bool IsMapReady { get; set; } = true;
	public string SessionToken { get; set; } = Guid.NewGuid().ToString();
	public bool IsDisconnected { get; set; } = false;
}