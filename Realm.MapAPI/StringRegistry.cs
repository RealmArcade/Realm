using System;
using System.Collections.Generic;

namespace Realm.MapAPI;

/// <summary>
/// Provides high-performance string interning and integer identifier mappings for unmanaged ECS components.
/// </summary>
public static class StringRegistry
{
	private static readonly List<string> _idToString = new List<string> { "" };
	private static readonly Dictionary<string, int> _stringToId = new Dictionary<string, int>(StringComparer.Ordinal) { { "", 0 } };
	private static readonly object _lock = new object();

	/// <summary>
	/// Gets or registers a string in the registry and returns its unique integer identifier.
	/// </summary>
	/// <param name="str">The string to intern.</param>
	/// <returns>The integer string identifier, or 0 if null or empty.</returns>
	public static int GetOrAdd(string? str)
	{
		if (string.IsNullOrEmpty(str)) return 0;
		lock (_lock)
		{
			if (_stringToId.TryGetValue(str, out int id)) return id;
			id = _idToString.Count;
			_idToString.Add(str);
			_stringToId[str] = id;
			return id;
		}
	}

	/// <summary>
	/// Retrieves the interned string associated with the specified identifier.
	/// </summary>
	/// <param name="id">The integer string identifier.</param>
	/// <returns>The string value, or empty string if not found.</returns>
	public static string Get(int id)
	{
		if (id <= 0) return string.Empty;
		lock (_lock)
		{
			return (id < _idToString.Count) ? _idToString[id] : string.Empty;
		}
	}

	/// <summary>
	/// Resets the string registry, clearing all interned string values.
	/// </summary>
	public static void Reset()
	{
		lock (_lock)
		{
			_idToString.Clear();
			_idToString.Add("");
			_stringToId.Clear();
			_stringToId[""] = 0;
		}
	}
}
