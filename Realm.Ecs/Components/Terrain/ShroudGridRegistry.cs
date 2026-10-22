using System.Collections.Generic;

namespace Realm.Ecs.Components.Terrain;

public static class ShroudGridRegistry
{
	private static readonly List<ShroudGridData> Store = new() { null };
	private static readonly Queue<int> FreeHandles = new();

	static ShroudGridRegistry()
	{
		ColdPayloadRegistry.RegisterResetAction(Reset);
	}

	public static int Create()
	{
		var grid = new ShroudGridData();

		if (FreeHandles.Count > 0)
		{
			int reusedHandle = FreeHandles.Dequeue();
			Store[reusedHandle] = grid;
			return reusedHandle;
		}

		int handle = Store.Count;
		Store.Add(grid);
		return handle;
	}

	public static ShroudGridData Get(int handle)
	{
		if (handle <= 0 || handle >= Store.Count) return null;
		return Store[handle];
	}

	public static void Release(int handle)
	{
		if (handle <= 0 || handle >= Store.Count) return;

		var grid = Store[handle];
		if (grid == null) return;

		grid.Release();
		Store[handle] = null;
		FreeHandles.Enqueue(handle);
	}

	public static void Reset()
	{
		for (int handle = 1; handle < Store.Count; handle++)
		{
			var grid = Store[handle];
			if (grid == null) continue;
			grid.Release();
			Store[handle] = null;
		}

		Store.RemoveRange(1, Store.Count - 1);
		FreeHandles.Clear();
	}
}
