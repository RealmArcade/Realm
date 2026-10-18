using System;
using System.Collections.Generic;

namespace Realm.Ecs;

public static class StringRegistry
{
	private static readonly List<string> _idToString = new List<string> { "" };
	private static readonly Dictionary<string, int> _stringToId = new Dictionary<string, int>(StringComparer.Ordinal) { { "", 0 } };
	private static readonly object _lock = new object();

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

	public static string Get(int id)
	{
		if (id <= 0) return string.Empty;
		lock (_lock)
		{
			return (id < _idToString.Count) ? _idToString[id] : string.Empty;
		}
	}

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
