using MemoryPack;

namespace Realm.Shared.Network;

[MemoryPackable]
public partial struct MatchPauseState
{
	public bool Paused;

	public int RequestedByPlayer;
}
