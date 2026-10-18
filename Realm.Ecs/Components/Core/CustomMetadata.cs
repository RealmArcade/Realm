using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct CustomMetadata
{
	public int Handle;

	public CustomMetadata(Dictionary<string, object> value)
	{
		Handle = ColdPayloadRegistry<Dictionary<string, object>>.Register(value);
	}

	public Dictionary<string, object> Value
	{
		get => ColdPayloadRegistry<Dictionary<string, object>>.Get(Handle);
		set => Handle = ColdPayloadRegistry<Dictionary<string, object>>.Register(value);
	}
}
