using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Cooldowns
{
	public int Handle;

	public Cooldowns(Dictionary<string, float> value)
	{
		Handle = ColdPayloadRegistry<Dictionary<string, float>>.Register(value);
	}

	public Dictionary<string, float> Value
	{
		get => ColdPayloadRegistry<Dictionary<string, float>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<Dictionary<string, float>>.Register(value);
	}
}
