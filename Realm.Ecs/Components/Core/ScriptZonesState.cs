using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct ScriptZonesState
{
	public int Handle;

	public ScriptZonesState(List<ZoneBounds> zones)
	{
		Handle = ColdPayloadRegistry<List<ZoneBounds>>.Register(zones);
	}

	public List<ZoneBounds> Zones => ColdPayloadRegistry<List<ZoneBounds>>.Get(Handle);
}
