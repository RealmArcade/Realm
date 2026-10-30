namespace Realm.Ecs.Components.Terrain;

/// <summary>
///     Resolves a shroud grid handle in the ambient world's own store. Only one world is alive per process at a time,
///     so a component can name its grid without holding a world reference, and a second world constructed in the same
///     process never aliases or frees the first world's grids. The world and the store resolved from it are published
///     together as one binding, because a reader that paired a new ambient world with a store cached beside the
///     previous one would resolve another world's handles.
/// </summary>
public static class ShroudGridRegistry
{
	private class StoreBinding
	{
		public StoreBinding(EcsWorld? world, ShroudGridStore store)
		{
			World = world;
			Store = store;
		}

		public EcsWorld? World { get; }

		public ShroudGridStore Store { get; }
	}

	private static StoreBinding? _cachedBinding;
	private static ShroudGridStore? _worldlessStore;

	private static ShroudGridStore Store
	{
		get
		{
			EcsWorld? ambient = EcsWorld.Ambient;
			StoreBinding? cached = Volatile.Read(ref _cachedBinding);
			if (cached != null && ReferenceEquals(cached.World, ambient)) return cached.Store;

			return Resolve(ambient);
		}
	}

	private static ShroudGridStore Resolve(EcsWorld? ambient)
	{
		if (ambient == null) return _worldlessStore ??= new ShroudGridStore();

		ShroudGridStore store = ambient.PayloadStores.ShroudGrids;
		Volatile.Write(ref _cachedBinding, new StoreBinding(ambient, store));
		return store;
	}

	public static int Create() => Store.Create();

	public static ShroudGridData? Get(int handle) => Store.Get(handle);

	public static void Release(int handle) => Store.Release(handle);
}
