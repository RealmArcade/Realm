namespace Realm.MapAPI;

/// <summary>
/// Defines the base lifecycle methods for map scripts.
/// </summary>
public interface IMapScript
{
    /// <summary>
    /// Called when the map is first loaded to set up initial entities, rules, and resources.
    /// </summary>
    /// <param name="api">The game API reference to manipulate the game world.</param>
    void Initialize(IGameAPI api);

    /// <summary>
    /// Called on every simulation physics tick to update map-specific logic.
    /// </summary>
    /// <param name="api">The game API reference to manipulate the game world.</param>
    /// <param name="delta">The time elapsed since the last physics tick, in seconds.</param>
    void Update(IGameAPI api, float delta);
}