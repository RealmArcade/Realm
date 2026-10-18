using System;
using System.Threading;

namespace Realm.Ecs;

public static class ComponentTypeRegistry
{
	private static int _nextId = 0;

	public static int GetId<T>() => ComponentType<T>.Id;

	public static int AssignId()
	{
		int id = Interlocked.Increment(ref _nextId) - 1;
		if (id >= 256)
		{
			throw new InvalidOperationException($"Exceeded maximum component type limit of 256. Attempted ID: {id}");
		}
		return id;
	}

	public static int Count => _nextId;
}

public static class ComponentType<T>
{
	public static readonly int Id = ComponentTypeRegistry.AssignId();
}
