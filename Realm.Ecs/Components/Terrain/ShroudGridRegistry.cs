namespace Realm.Ecs.Components.Terrain;

/// <summary>
///     Resolves a shroud grid handle in the ambient world's own store. Only one world is alive per process at a time,
///     so a component can name its grid without holding a world reference, and a second world constructed in the same
///     process never aliases or frees the first world's grids.
/// </summary>
public static class ShroudGridRegistry
{
	private static EcsWorld? _cachedWorld;
	private static ShroudGridStore? _cachedStore;
	private static ShroudGridStore? _worldlessStore;

	private static ShroudGridStore Store
	{
		get
		{
			EcsWorld? ambient = EcsWorld.Ambient;
			if (!ReferenceEquals(ambient, _cachedWorld))
			{
				_cachedWorld = ambient;
				_cachedStore = ambient?.PayloadStores.ShroudGrids;
			}

			return _cachedStore ?? (_worldlessStore ??= new ShroudGridStore());
		}
	}

	public static int Create() => Store.Create();

	public static ShroudGridData? Get(int handle) => Store.Get(handle);

	public static void Release(int handle) => Store.Release(handle);
}
