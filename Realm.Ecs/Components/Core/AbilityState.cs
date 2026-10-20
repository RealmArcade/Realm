using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds a list of active ability names for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct AbilityState
{
	public int Handle;

	public AbilityState(List<string> abilities)
	{
		Handle = ColdPayloadRegistry<List<string>>.Register(abilities);
	}

	public List<string> Abilities
	{
		get => ColdPayloadRegistry<List<string>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<List<string>>.Register(value);
	}
}
