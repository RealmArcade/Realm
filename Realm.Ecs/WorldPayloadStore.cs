using System;
using System.Collections.Generic;
using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs;

public class ColdPayloadStore<T> where T : class
{
	private readonly List<T?> _store = new List<T?> { null };
	private readonly Queue<int> _freeList = new Queue<int>();
	private readonly Dictionary<string, int> _handlesByContentKey = new Dictionary<string, int>(StringComparer.Ordinal);
	private readonly Dictionary<int, string> _contentKeyByHandle = new Dictionary<int, string>();
	private readonly Dictionary<int, int> _referenceCounts = new Dictionary<int, int>();
	private readonly object _lock = new object();

	public int Register(T? payload)
	{
		if (payload == null) return 0;
		lock (_lock)
		{
			return Allocate(payload);
		}
	}

	public int Share(T? payload, string contentKey)
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

	public bool IsShared(int handle)
	{
		if (handle <= 0) return false;
		lock (_lock)
		{
			return _referenceCounts.ContainsKey(handle);
		}
	}

	public int GetReferenceCount(int handle)
	{
		if (handle <= 0) return 0;
		lock (_lock)
		{
			return _referenceCounts.TryGetValue(handle, out int count) ? count : 0;
		}
	}

	public void Free(int handle)
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

	public T? Get(int handle)
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

	public void Set(int handle, T payload)
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

	public void Reset()
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

	private int Allocate(T payload)
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

public class ShroudGridStore
{
	private readonly List<ShroudGridData?> _store = new List<ShroudGridData?> { null };
	private readonly Queue<int> _freeHandles = new Queue<int>();

	public int Create()
	{
		var grid = new ShroudGridData();

		if (_freeHandles.Count > 0)
		{
			int reusedHandle = _freeHandles.Dequeue();
			_store[reusedHandle] = grid;
			return reusedHandle;
		}

		int handle = _store.Count;
		_store.Add(grid);
		return handle;
	}

	public ShroudGridData? Get(int handle)
	{
		if (handle <= 0 || handle >= _store.Count) return null;
		return _store[handle];
	}

	public void Release(int handle)
	{
		if (handle <= 0 || handle >= _store.Count) return;

		ShroudGridData? grid = _store[handle];
		if (grid == null) return;

		grid.Release();
		_store[handle] = null;
		_freeHandles.Enqueue(handle);
	}

	public void Reset()
	{
		for (int handle = 1; handle < _store.Count; handle++)
		{
			ShroudGridData? grid = _store[handle];
			if (grid == null) continue;
			grid.Release();
			_store[handle] = null;
		}

		_store.RemoveRange(1, _store.Count - 1);
		_freeHandles.Clear();
	}
}

public sealed class WorldPayloadStores
{
	private readonly Dictionary<Type, object> _coldPayloadStoresByType = new Dictionary<Type, object>();
	private readonly List<Action> _resetActions = new List<Action>();
	private readonly object _lock = new object();

	public ShroudGridStore ShroudGrids { get; }

	public WorldPayloadStores()
	{
		ShroudGrids = new ShroudGridStore();
		_resetActions.Add(ShroudGrids.Reset);
	}

	public ColdPayloadStore<T> GetOrCreateColdPayloadStore<T>() where T : class
	{
		lock (_lock)
		{
			if (_coldPayloadStoresByType.TryGetValue(typeof(T), out object? existing))
			{
				return (ColdPayloadStore<T>)existing;
			}

			var store = new ColdPayloadStore<T>();
			_coldPayloadStoresByType[typeof(T)] = store;
			_resetActions.Add(store.Reset);
			return store;
		}
	}

	public void Reset()
	{
		lock (_lock)
		{
			for (int index = 0; index < _resetActions.Count; index++)
			{
				_resetActions[index]();
			}
		}
	}
}
