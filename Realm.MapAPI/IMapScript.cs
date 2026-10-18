namespace Realm.MapAPI;

/// <summary>
/// Defines the base lifecycle methods for map scripts, supporting both direct <see cref="IGameAPI"/> parameter passing and global <see cref="GameAPI.Current"/> singleton usage.
/// </summary>
public interface IMapScript
{
	/// <summary>
	/// Called when the map is first loaded to set up initial entities, rules, and resources.
	/// </summary>
	/// <param name="api">The game API reference to manipulate the game world.</param>
	void Initialize(IGameAPI api) => Initialize();

	/// <summary>
	/// Called when the map is first loaded using <see cref="GameAPI.Current"/>.
	/// </summary>
	void Initialize() {}

	/// <summary>
	/// Called on every simulation physics tick to update map-specific logic.
	/// </summary>
	/// <param name="api">The game API reference to manipulate the game world.</param>
	/// <param name="delta">The time elapsed since the last physics tick, in seconds.</param>
	void Update(IGameAPI api, float delta) => Update(delta);

	/// <summary>
	/// Called on every simulation physics tick using <see cref="GameAPI.Current"/>.
	/// </summary>
	/// <param name="delta">The time elapsed since the last physics tick, in seconds.</param>
	void Update(float delta) {}
}