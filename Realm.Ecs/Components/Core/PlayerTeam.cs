using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Associates an entity with a team index and team name identifier.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PlayerTeam
{
	public int TeamIndex;
	public int TeamNameStringId;

	public PlayerTeam(int teamIndex, int teamNameStringId = 0)
	{
		TeamIndex = teamIndex;
		TeamNameStringId = teamNameStringId;
	}

	public string TeamName
	{
		get => StringRegistry.Get(TeamNameStringId);
		set => TeamNameStringId = StringRegistry.GetOrAdd(value);
	}
}
