using System;
using System.Collections.Generic;
using Realm.Ecs.Components.Terrain;

namespace Realm.Ecs;

/// <summary>
///     The payloads a world keeps outside its component arrays, addressed by handle. The simulation thread is the only
///     writer, because handles are allocated, shared and released as entities and components come and go. Readers are not
///     confined to it - a publish lane resolving the payload behind a component handle reads here too - so a write
///     publishes a new immutable view of the table and a read takes one volatile read of it, instead of every reader
///     taking the writer's lock.
/// </summary>
public class ColdPayloadStore<T> where T : class
{
	private readonly object _writeLock = new object();
	private readonly Stack<int> _freeHandles = new Stack<int>();
	private readonly Dictionary<string, int> _handlesByContentKey = new Dictionary<string, int>(StringComparer.Ordinal);
	private readonly Dictionary<int, string> _contentKeyByHandle = new Dictionary<int, string>();
	private readonly Dictionary<int, int> _referenceCounts = new Dictionary<int, int>();

	private T?[] _published = new T?[1];
	private int _nextHandle = 1;

	public int Register(T? payload)
	{
		if (payload == null) return 0;

		lock (_writeLock)
		{
			return Allocate(payload);
		}
	}

	public int Share(T? payload, string contentKey)
	{
		if (payload == null) return 0;
		if (string.IsNullOrEmpty(contentKey)) return Register(payload);

		lock (_writeLock)
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
		lock (_writeLock)
		{
			return _referenceCounts.ContainsKey(handle);
		}
	}

	public int GetReferenceCount(int handle)
	{
		if (handle <= 0) return 0;
		lock (_writeLock)
		{
			return _referenceCounts.TryGetValue(handle, out int count) ? count : 0;
		}
	}

	public void Free(int handle)
	{
		if (handle <= 0) return;
		lock (_writeLock)
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

			T?[] published = Volatile.Read(ref _published);
			if ((uint)handle >= (uint)published.Length || published[handle] == null) return;

			Publish(handle, null);
			_freeHandles.Push(handle);
		}
	}

	public T? Get(int handle)
	{
		if (handle <= 0) return null;

		T?[] published = Volatile.Read(ref _published);
		return (uint)handle < (uint)published.Length ? published[handle] : null;
	}

	public void Set(int handle, T payload)
	{
		if (handle <= 0) return;
		lock (_writeLock)
		{
			Publish(handle, payload);
		}
	}

	public void Reset()
	{
		lock (_writeLock)
		{
			Volatile.Write(ref _published, new T?[1]);
			_nextHandle = 1;
			_freeHandles.Clear();
			_handlesByContentKey.Clear();
			_contentKeyByHandle.Clear();
			_referenceCounts.Clear();
		}
	}

	private int Allocate(T payload)
	{
		int handle = _freeHandles.Count > 0 ? _freeHandles.Pop() : _nextHandle++;
		Publish(handle, payload);
		return handle;
	}

	private void Publish(int handle, T? payload)
	{
		T?[] current = Volatile.Read(ref _published);
		T?[] next;
		if (handle >= current.Length)
		{
			next = new T?[Math.Max(handle + 1, current.Length * 2)];
			Array.Copy(current, next, current.Length);
		}
		else
		{
			next = (T?[])current.Clone();
		}

		next[handle] = payload;
		Volatile.Write(ref _published, next);
	}
}

/// <summary>
///     The shroud grids a world owns, addressed by handle. The simulation thread is the only writer, and the grid behind
///     a handle is created and released with the entity that carries it, so the store needs no coordination of its own.
/// </summary>
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

/// <summary>
///     The set of payload stores one world owns. Constructing a store for a type is the only thing that happens lazily,
///     and it happens on the thread that resolved the handle, so the type map takes a lock; the stores it hands out are
///     then read without one.
/// </summary>
public class WorldPayloadStores
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
