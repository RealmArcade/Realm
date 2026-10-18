using System.Runtime.InteropServices;

namespace Realm.MapAPI;

/// <summary>
/// A type-safe identifier wrapper for a resource category.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct ResourceId
{
	/// <summary>
	/// Gets the canonical string name of the resource.
	/// </summary>
	public string Value { get; init; }

	/// <summary>
	/// Initializes a new instance of the <see cref="ResourceId"/> struct.
	/// </summary>
	/// <param name="value">The resource category name.</param>
	public ResourceId(string value)
	{
		Value = value ?? string.Empty;
	}

	/// <summary>
	/// Implicitly converts a string resource name into a <see cref="ResourceId"/>.
	/// </summary>
	/// <param name="value">The resource category name.</param>
	public static implicit operator ResourceId(string value) => new(value);

	/// <summary>
	/// Implicitly converts a <see cref="ResourceId"/> into its string representation.
	/// </summary>
	/// <param name="id">The resource identifier.</param>
	public static implicit operator string(ResourceId id) => id.Value;
}
