using MemoryPack;
using System.Collections.Generic;

namespace Realm.Client.ReplaySystem;

[MemoryPackable]
public partial class ReplayFrame
{
	public int Tick { get; set; }
	public bool IsKeyframe { get; set; }
	public List<ReplayUnitSnapshot> Units { get; set; } = new();
	public ReplayPlayerResourceSnapshot Resources { get; set; }
	public List<ReplayProjectileSnapshot> Projectiles { get; set; } = new();
}