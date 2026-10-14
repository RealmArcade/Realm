using System.Collections.Generic;
using MemoryPack;

namespace Realm.Client.ReplaySystem;

[MemoryPackable]
public partial class ReplayHeader
{
	public string Magic { get; set; }
	public int Version { get; set; }
	public string MapName { get; set; }
	public string GameVersion { get; set; }
	public int TotalTicks { get; set; }
	public long Timestamp { get; set; }
	public List<ReplayPlayerInfo> Players { get; set; } = new();
}