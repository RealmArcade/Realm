namespace Realm.Ecs;

/// <summary>
///     Resolves a cold payload handle in the ambient world's own store. Only one world is alive per process at a time,
///     so a component can name its payload without holding a world reference, and a second world constructed in the
///     same process never aliases or frees the first world's payloads. The world and the store resolved from it are
///     published together as one binding, because a reader that paired a new ambient world with a store cached beside
///     the previous one would resolve another world's handles.
/// </summary>
public static class ColdPayloadRegistry<T> where T : class
{
	private class StoreBinding
	{
		public StoreBinding(EcsWorld? world, ColdPayloadStore<T> store)
		{
			World = world;
			Store = store;
		}

		public EcsWorld? World { get; }

		public ColdPayloadStore<T> Store { get; }
	}

	private static StoreBinding? _cachedBinding;
	private static ColdPayloadStore<T>? _worldlessStore;

	private static ColdPayloadStore<T> Store
	{
		get
		{
			EcsWorld? ambient = EcsWorld.Ambient;
			StoreBinding? cached = Volatile.Read(ref _cachedBinding);
			if (cached != null && ReferenceEquals(cached.World, ambient)) return cached.Store;

			return Resolve(ambient);
		}
	}

	private static ColdPayloadStore<T> Resolve(EcsWorld? ambient)
	{
		if (ambient == null) return _worldlessStore ??= new ColdPayloadStore<T>();

		ColdPayloadStore<T> store = ambient.PayloadStores.GetOrCreateColdPayloadStore<T>();
		Volatile.Write(ref _cachedBinding, new StoreBinding(ambient, store));
		return store;
	}

	public static int Register(T? payload) => Store.Register(payload);

	public static int Share(T? payload, string contentKey) => Store.Share(payload, contentKey);

	public static bool IsShared(int handle) => Store.IsShared(handle);

	public static int GetReferenceCount(int handle) => Store.GetReferenceCount(handle);

	public static void Free(int handle) => Store.Free(handle);

	public static T? Get(int handle) => Store.Get(handle);

	public static void Set(int handle, T payload) => Store.Set(handle, payload);
}
