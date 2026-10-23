using System.Runtime.InteropServices;
using Realm.MapAPI;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Holds the canonical definition identifier string for an entity template.
/// </summary>
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
