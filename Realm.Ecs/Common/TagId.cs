using System.Runtime.InteropServices;
using Realm.MapAPI;

namespace Realm.Ecs.Common;

/// <summary>
///     A type-safe wrapper for a Tag ID.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct TagId
{
	public int StringId { get; }

	public TagId(string value)
	{
		StringId = StringRegistry.GetOrAdd(value);
	}

	public string Value => StringRegistry.Get(StringId);
}
