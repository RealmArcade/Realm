using System.Runtime.InteropServices;

namespace Realm.Ecs.Common;

/// <summary>
///     A type-safe wrapper for a Resource ID.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct ResourceId
{
	public int StringId { get; }

	public ResourceId(string value)
	{
		StringId = StringRegistry.GetOrAdd(value);
	}

	public string Value => StringRegistry.Get(StringId);
}
