using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

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
