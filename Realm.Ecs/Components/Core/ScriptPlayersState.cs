using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Stores the simulation state for all 25 script players (slots 0..24).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ScriptPlayersState
{
	public int Handle;

	public ScriptPlayersState(ScriptPlayer[] players)
	{
		Handle = ColdPayloadRegistry<ScriptPlayer[]>.Register(players);
	}

	public ScriptPlayer[] Players
	{
		get => ColdPayloadRegistry<ScriptPlayer[]>.Get(Handle)!;
		set => Handle = ColdPayloadRegistry<ScriptPlayer[]>.Register(value);
	}
}
