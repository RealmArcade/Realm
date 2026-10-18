using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Meta;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Name
{
	public int StringId;

	public Name(string value)
	{
		StringId = StringRegistry.GetOrAdd(value);
	}

	public string Value
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
