using System.Runtime.InteropServices;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Meta;

/// <summary>
/// Holds the human-readable display name string for an entity.
/// </summary>
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
