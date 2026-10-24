using System;
using System.Collections.Generic;

namespace Realm.Ecs;

public static class ColdPayloadRegistry
{
	private static readonly List<Action> _resetActions = new();
	private static readonly object _resetLock = new();

	public static void RegisterResetAction(Action resetAction)
	{
		lock (_resetLock)
		{
			_resetActions.Add(resetAction);
		}
	}

	public static void ResetAll()
	{
		lock (_resetLock)
		{
			for (int index = 0; index < _resetActions.Count; index++)
			{
				_resetActions[index]();
			}
		}
	}
}

public static class ColdPayloadRegistry<T> where T : class
{
	private static readonly List<T?> _store = new List<T?> { null };
	private static readonly Queue<int> _freeList = new Queue<int>();
	private static readonly Dictionary<string, int> _handlesByContentKey = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly Dictionary<int, string> _contentKeyByHandle = new Dictionary<int, string>();
	private static readonly Dictionary<int, int> _referenceCounts = new Dictionary<int, int>();
	private static readonly object _lock = new object();

	static ColdPayloadRegistry()
	{
		ColdPayloadRegistry.RegisterResetAction(Reset);
	}

	public static int Register(T? payload)
	{
		if (payload == null) return 0;
		lock (_lock)
		{
			return Allocate(payload);
		}
	}

	public static int Share(T? payload, string contentKey)
	{
		if (payload == null) return 0;
		if (string.IsNullOrEmpty(contentKey)) return Register(payload);

		lock (_lock)
		{
			if (_handlesByContentKey.TryGetValue(contentKey, out int existingHandle))
			{
				_referenceCounts[existingHandle] = (_referenceCounts.TryGetValue(existingHandle, out int count) ? count : 1) + 1;
				return existingHandle;
			}

			int handle = Allocate(payload);
			_handlesByContentKey[contentKey] = handle;
			_contentKeyByHandle[handle] = contentKey;
			_referenceCounts[handle] = 1;
			return handle;
		}
	}

	public static bool IsShared(int handle)
	{
		if (handle <= 0) return false;
		lock (_lock)
		{
			return _referenceCounts.ContainsKey(handle);
		}
	}

	public static int GetReferenceCount(int handle)
	{
		if (handle <= 0) return 0;
		lock (_lock)
		{
			return _referenceCounts.TryGetValue(handle, out int count) ? count : 0;
		}
	}

	public static void Free(int handle)
	{
		if (handle <= 0) return;
		lock (_lock)
		{
			if (_referenceCounts.TryGetValue(handle, out int count))
			{
				if (count > 1)
				{
					_referenceCounts[handle] = count - 1;
					return;
				}

				_referenceCounts.Remove(handle);
				if (_contentKeyByHandle.TryGetValue(handle, out string? contentKey))
				{
					_contentKeyByHandle.Remove(handle);
					_handlesByContentKey.Remove(contentKey);
				}
			}

			if (handle < _store.Count && _store[handle] != null)
			{
				_store[handle] = null;
				_freeList.Enqueue(handle);
			}
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

	public static void Reset()
	{
		lock (_lock)
		{
			_store.Clear();
			_store.Add(null);
			_freeList.Clear();
			_handlesByContentKey.Clear();
			_contentKeyByHandle.Clear();
			_referenceCounts.Clear();
		}
	}

	private static int Allocate(T payload)
	{
		if (_freeList.Count > 0)
		{
			int reusedId = _freeList.Dequeue();
			_store[reusedId] = payload;
			return reusedId;
		}

		int id = _store.Count;
		_store.Add(payload);
		return id;
	}
}
