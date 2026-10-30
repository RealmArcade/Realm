using System.Runtime.InteropServices;

namespace Realm.Ecs.Common;

/// <summary>
/// A type-safe unmanaged identifier wrapper for a resource category.
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

	public static implicit operator ResourceId(string value) => new(value);
	public static implicit operator string(ResourceId id) => id.Value;
}
