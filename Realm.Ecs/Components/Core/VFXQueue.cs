using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds a queue of visual effect spawn requests associated with an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct VFXQueue
{
	public int Handle;

	public VFXQueue(List<VFXRequest> requests)
	{
		Handle = ColdPayloadRegistry<List<VFXRequest>>.Register(requests);
	}

	public List<VFXRequest> Requests
	{
		get => ColdPayloadRegistry<List<VFXRequest>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<List<VFXRequest>>.Register(value);
	}
}
