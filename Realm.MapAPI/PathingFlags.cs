using System;
using System.Text.Json.Serialization;

namespace Realm.MapAPI;

/// <summary>
/// Bit flags describing traversability and usage capabilities for terrain cells and navigation mesh regions.
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PathingFlags
{
	/// <summary>
	/// No flags enabled; completely impassable.
	/// </summary>
	None = 0,

	/// <summary>
	/// Shallow water traversal capability.
	/// </summary>
	ShallowWater = 1 << 0,

	/// <summary>
	/// Deep water traversal capability.
	/// </summary>
	DeepWater = 1 << 1,

	/// <summary>
	/// Aerial or flying traversal capability.
	/// </summary>
	Flying = 1 << 2,

	/// <summary>
	/// Standard ground unit traversal capability.
	/// </summary>
	Ground = 1 << 3,

	/// <summary>
	/// Structure buildability permission.
	/// </summary>
	Buildable = 1 << 5,

	/// <summary>
	/// All traversal and buildability flags enabled.
	/// </summary>
	All = 0xFF,
}
