using System;
using System.Collections.Generic;

namespace Realm.Ecs;

public static class ColdPayloadRegistry<T> where T : class
{
	private static readonly List<T?> _store = new List<T?> { null };
	private static readonly object _lock = new object();

	public static int Register(T? payload)
	{
		if (payload == null) return 0;
		lock (_lock)
		{
			int id = _store.Count;
			_store.Add(payload);
			return id;
		}
	}

	public static T? Get(int handle)
	{
		if (handle <= 0) return null;
		lock (_lock)
		{
			if (handle < _store.Count)
			{
				return _store[handle];
			}
			return null;
		}
	}

	public static void Set(int handle, T payload)
	{
		if (handle <= 0) return;
		lock (_lock)
		{
			while (_store.Count <= handle)
			{
				_store.Add(null);
			}
			_store[handle] = payload;
		}
	}
}
