// Extracted from EcsWorld.cs (task 6 decomposition).
using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Realm.Ecs.Services;

namespace Realm.Ecs;

public partial class EcsWorld
{
	public ref T Add<T>(Entity entity, in T component = default) where T : unmanaged
	{
		AssertNotForkedIntoWorkers("Adding a component");

		int id = entity.Id;
		int cId = ComponentType<T>.Id;
		var buffer = GetOrCreateBuffer<T>(cId);
		buffer.Set(id, component);

		int chunk = id >> 6;
		ulong bit = 1UL << (id & 63);
		_componentBitsets[cId][chunk] |= bit;

		_entityMasks.Set(id, _entityMasks.Get(id) | ComponentMask.FromId(cId));
		return ref buffer.Get(id);
	}

	public ref T AddOrGet<T>(Entity entity, in T component = default) where T : unmanaged
	{
		if (Has<T>(entity))
		{
			return ref Get<T>(entity);
		}
		return ref Add(entity, component);
	}

	public void Set<T>(Entity entity, in T component) where T : unmanaged
	{
		AssertNotForkedIntoWorkers("Setting a component");

		int id = entity.Id;
		int cId = ComponentType<T>.Id;
		var buffer = GetOrCreateBuffer<T>(cId);
		buffer.Set(id, component);

		int chunk = id >> 6;
		ulong bit = 1UL << (id & 63);
		_componentBitsets[cId][chunk] |= bit;

		_entityMasks.Set(id, _entityMasks.Get(id) | ComponentMask.FromId(cId));
	}

	public void SetOrAdd<T>(Entity entity, in T component) where T : unmanaged
	{
		Set<T>(entity, component);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T Get<T>(Entity entity) where T : unmanaged
	{
		int id = entity.Id;
		int cId = ComponentType<T>.Id;
		var buffer = (ComponentBuffer<T>)_buffers[cId];
		return ref buffer.Get(id);
	}

	public bool TryGet<T>(Entity entity, out T component) where T : unmanaged
	{
		if (Has<T>(entity))
		{
			component = Get<T>(entity);
			return true;
		}
		component = default;
		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Has<T>(Entity entity) where T : unmanaged
	{
		int id = entity.Id;
		if (id < 0 || id >= MAX_ENTITIES || _alive.Get(id) == 0) return false;
		int cId = ComponentType<T>.Id;
		int chunk = id >> 6;
		ulong bit = 1UL << (id & 63);
		ulong[]? componentBitset = Volatile.Read(ref _componentBitsets[cId]);
		return componentBitset != null && (componentBitset[chunk] & bit) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Has(Entity entity, ComponentMask mask)
	{
		int id = entity.Id;
		if (id < 0 || id >= MAX_ENTITIES || _alive.Get(id) == 0) return false;
		return _entityMasks.Get(id).HasAll(mask);
	}

	public void Remove<T>(Entity entity) where T : unmanaged
	{
		AssertNotForkedIntoWorkers("Removing a component");

		int id = entity.Id;
		if (id < 0 || id >= MAX_ENTITIES || _alive.Get(id) == 0) return;
		int cId = ComponentType<T>.Id;
		if (_componentBitsets[cId] == null) return;

		int chunk = id >> 6;
		ulong bit = 1UL << (id & 63);
		_componentBitsets[cId][chunk] &= ~bit;

		_buffers[cId]?.Clear(id);
		_entityMasks.Set(id, ClearMaskBit(_entityMasks.Get(id), cId));
	}

	public ComponentBuffer<T> GetBuffer<T>() where T : unmanaged
	{
		int cId = ComponentType<T>.Id;
		return GetOrCreateBuffer<T>(cId);
	}

	public void BindExternalComponentMemory<T>(IntPtr pointer, int lengthInBytes, int capacity = -1) where T : unmanaged
	{
		int cId = ComponentType<T>.Id;
		var buffer = GetOrCreateBuffer<T>(cId);
		buffer.BindExternalMemory(pointer, lengthInBytes, capacity);
	}
}
