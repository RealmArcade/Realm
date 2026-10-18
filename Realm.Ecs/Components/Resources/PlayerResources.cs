using System.Collections.Generic;
using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     A component holding all resource amounts for a player entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct PlayerResources
{
	public int Handle;

	public PlayerResources(Dictionary<ResourceId, int> value)
	{
		Handle = ColdPayloadRegistry<Dictionary<ResourceId, int>>.Register(value);
	}

	public Dictionary<ResourceId, int> Value
	{
		get => ColdPayloadRegistry<Dictionary<ResourceId, int>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<Dictionary<ResourceId, int>>.Register(value);
	}
}
