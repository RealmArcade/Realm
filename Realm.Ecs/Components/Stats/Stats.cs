using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
/// Holds a dictionary mapping stat IDs to current stat values for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Stats
{
	public int Handle;

	public Stats(Dictionary<StatId, float> value)
	{
		Handle = ColdPayloadRegistry<Dictionary<StatId, float>>.Register(value);
	}

	public Dictionary<StatId, float> Value
	{
		get => ColdPayloadRegistry<Dictionary<StatId, float>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<Dictionary<StatId, float>>.Register(value);
	}
}
