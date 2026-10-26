namespace Realm.Ecs;

/// <summary>
///     Resolves a cold payload handle in the ambient world's own store. Only one world is alive per process at a time,
///     so a component can name its payload without holding a world reference, and a second world constructed in the
///     same process never aliases or frees the first world's payloads.
/// </summary>
public static class ColdPayloadRegistry<T> where T : class
{
	private static EcsWorld? _cachedWorld;
	private static ColdPayloadStore<T>? _cachedStore;
	private static ColdPayloadStore<T>? _worldlessStore;

	private static ColdPayloadStore<T> Store
	{
		get
		{
			EcsWorld? ambient = EcsWorld.Ambient;
			if (!ReferenceEquals(ambient, _cachedWorld))
			{
				_cachedWorld = ambient;
				_cachedStore = ambient?.PayloadStores.GetOrCreateColdPayloadStore<T>();
			}

			return _cachedStore ?? (_worldlessStore ??= new ColdPayloadStore<T>());
		}
	}

	public static int Register(T? payload) => Store.Register(payload);

	public static int Share(T? payload, string contentKey) => Store.Share(payload, contentKey);

	public static bool IsShared(int handle) => Store.IsShared(handle);

	public static int GetReferenceCount(int handle) => Store.GetReferenceCount(handle);

	public static void Free(int handle) => Store.Free(handle);

	public static T? Get(int handle) => Store.Get(handle);

	public static void Set(int handle, T payload) => Store.Set(handle, payload);
}
