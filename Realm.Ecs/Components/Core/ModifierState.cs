using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Components.Stats;

namespace Realm.Ecs.Components.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ModifierState
{
	public int Handle;

	public ModifierState(List<StatModifier> value)
	{
		Handle = ColdPayloadRegistry<List<StatModifier>>.Register(value);
	}

	public List<StatModifier> Value
	{
		get => ColdPayloadRegistry<List<StatModifier>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<List<StatModifier>>.Register(value);
	}
}
