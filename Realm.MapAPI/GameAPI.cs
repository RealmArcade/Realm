namespace Realm.MapAPI;

/// <summary>
/// Provides global access to the current <see cref="IGameAPI"/> instance within the WebAssembly or host runtime execution context.
/// </summary>
public static class GameAPI
{
	/// <summary>
	/// Gets or sets the active <see cref="IGameAPI"/> instance.
	/// </summary>
	public static IGameAPI Current { get; set; } = null!;
}
