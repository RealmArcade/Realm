using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Core;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct DefinitionId
{
	public int StringId;

	public DefinitionId(string value)
	{
		StringId = StringRegistry.GetOrAdd(value);
	}

	public string Value
	{
		get => StringRegistry.Get(StringId);
		set => StringId = StringRegistry.GetOrAdd(value);
	}
}
