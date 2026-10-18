using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Realm.Ecs;

public unsafe class ComponentBuffer<T> : IComponentBuffer where T : unmanaged
{
	private T[]? _pinnedArray;
	private T* _pointer;
	private bool _isExternal;
	private readonly int _capacity;

	public int ComponentId { get; }
	public bool IsExternal => _isExternal;

	public ComponentBuffer(int capacity = EcsWorld.MAX_ENTITIES)
	{
		_capacity = capacity;
		ComponentId = ComponentType<T>.Id;
		_pinnedArray = GC.AllocateArray<T>(capacity, pinned: true);
		_pointer = (T*)Unsafe.AsPointer(ref _pinnedArray[0]);
		_isExternal = false;
	}

	public void BindExternalMemory(IntPtr pointer, int lengthInBytes)
	{
		int requiredBytes = _capacity * sizeof(T);
		if (lengthInBytes < requiredBytes)
		{
			throw new ArgumentException($"Buffer size {lengthInBytes} bytes is less than required {requiredBytes} bytes for {typeof(T).Name}");
		}
		_pinnedArray = null;
		_pointer = (T*)pointer.ToPointer();
		_isExternal = true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref T Get(int entityId)
	{
		return ref _pointer[entityId];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Set(int entityId, in T value)
	{
		_pointer[entityId] = value;
	}

	public Action<int>? ClearHandler { get; set; }

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Clear(int entityId)
	{
		ClearHandler?.Invoke(entityId);
		_pointer[entityId] = default;
	}

	public Span<T> AsSpan()
	{
		return new Span<T>(_pointer, _capacity);
	}
}
