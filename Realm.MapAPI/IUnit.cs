using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Represents a safe, high-level interface for unit interaction available to map scripts.
/// </summary>
public interface IUnit
{
    /// <summary>
    /// Gets a unique runtime identifier for this unit instance.
    /// </summary>
    int UniqueId { get; }

    /// <summary>
    /// Gets the unique canonical object identifier for the unit's type or archetype (format: object_type/slug, e.g. unit/worker, building/barracks).
    /// </summary>
    string ObjectID { get; }

    /// <summary>
    /// Gets the unique identifier for the unit's type or archetype.
    /// </summary>
    string UnitId => ObjectID;

    /// <summary>
    /// Gets or sets the display name of the unit.
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit is owned by an enemy player.
    /// </summary>
    bool IsEnemy { get; set; }

    /// <summary>
    /// Gets or sets the zero-based player slot index (0-24) that owns this unit.
    /// </summary>
    int Player { get; set; }

    /// <summary>
    /// Gets a value indicating whether the unit is a structural building.
    /// </summary>
    bool IsBuilding { get; }

    /// <summary>
    /// Gets the current position of the unit in 3D world space.
    /// </summary>
    Vector3 Position { get; }

    /// <summary>
    /// Gets or sets the current health point value of the unit.
    /// </summary>
    float Health { get; set; }

    /// <summary>
    /// Gets or sets the maximum health point capacity of the unit.
    /// </summary>
    float MaxHealth { get; set; }

    /// <summary>
    /// Gets or sets the base attack damage of the unit.
    /// </summary>
    float Damage { get; set; }

    /// <summary>
    /// Gets or sets the attack range of the unit.
    /// </summary>
    float Range { get; set; }

    /// <summary>
    /// Gets or sets the armor rating of the unit.
    /// </summary>
    float Armor { get; set; }

    /// <summary>
    /// Gets or sets the attack speed multiplier of the unit. A value of 1 uses the unit's base attack cooldown,
    /// 2 attacks twice as often, and 0.5 attacks half as often. Values less than or equal to 0 are ignored.
    /// </summary>
    float AttackSpeed { get; set; }

    /// <summary>
    /// Gets or sets the mana regeneration of the unit in mana points restored per second.
    /// </summary>
    float ManaRegen { get; set; }

    /// <summary>
    /// Gets or sets the movement speed of the unit.
    /// </summary>
    float Speed { get; set; }

    /// <summary>
    /// Gets a value indicating whether the unit is a Hero unit.
    /// </summary>
    bool IsHero { get; }

    /// <summary>
    /// Gets or sets the level of the hero unit.
    /// </summary>
    int Level { get; set; }

    /// <summary>
    /// Gets or sets the experience points of the hero unit.
    /// </summary>
    float Experience { get; set; }

    /// <summary>
    /// Gets the experience bounty awarded for defeating this unit.
    /// </summary>
    float XpBounty { get; }

    /// <summary>
    /// Gets the gold bounty awarded for defeating this unit.
    /// </summary>
    float GoldBounty { get; }

    /// <summary>
    /// Gets a value indicating whether the unit is dead.
    /// </summary>
    bool IsDead { get; }

    /// <summary>
    /// Orders the unit to move to the specified destination coordinates.
    /// </summary>
    /// <param name="destination">The destination position in world coordinates.</param>
    void MoveTo(Vector3 destination);

    /// <summary>
    /// Orders the unit to attack-move towards the specified destination.
    /// </summary>
    /// <param name="destination">The target coordinates for attack-moving.</param>
    void AttackMove(Vector3 destination);

    /// <summary>
    /// Orders the unit to attack the specified target unit.
    /// </summary>
    /// <param name="target">The target unit to attack.</param>
    void Attack(IUnit target);

    /// <summary>
    /// Orders the unit (if it is a worker) to harvest resources from the specified resource node.
    /// </summary>
    /// <param name="resourceNode">The resource node to harvest.</param>
    void Gather(IResourceNode resourceNode);

    /// <summary>
    /// Instantly teleports the unit to the specified destination coordinates.
    /// </summary>
    /// <param name="position">The coordinates in 3D world space to teleport the unit to.</param>
    void Teleport(Vector3 position);

    /// <summary>
    /// Orders the unit to move to the center of the specified coordinate region.
    /// </summary>
    /// <param name="destination">The destination coordinate region.</param>
    void MoveTo(Coordinate destination) => MoveTo(destination.Center);

    /// <summary>
    /// Orders the unit to attack-move towards the center of the specified coordinate region.
    /// </summary>
    /// <param name="destination">The target coordinate region for attack-moving.</param>
    void AttackMove(Coordinate destination) => AttackMove(destination.Center);

    /// <summary>
    /// Instantly teleports the unit to the center of the specified coordinate region.
    /// </summary>
    /// <param name="position">The target coordinate region to teleport the unit to.</param>
    void Teleport(Coordinate position) => Teleport(position.Center);

    /// <summary>
    /// Gets or sets the current mana points of the unit.
    /// </summary>
    float Mana { get; set; }

    /// <summary>
    /// Gets or sets the maximum mana capacity of the unit.
    /// </summary>
    float MaxMana { get; set; }

    /// <summary>
    /// Gets or sets the visual scaling factor of the unit.
    /// </summary>
    float Scale { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the unit is invulnerable to damage.
    /// </summary>
    bool Invulnerable { get; set; }

    /// <summary>
    /// Orders the unit to stop its current action immediately.
    /// </summary>
    void Stop();

    /// <summary>
    /// Orders the unit to hold its ground and defend itself without moving.
    /// </summary>
    void HoldPosition();

    /// <summary>
    /// Stuns the unit, disabling its movement and actions for a duration.
    /// </summary>
    /// <param name="duration">The stun duration in seconds.</param>
    void Stun(float duration);

    /// <summary>
    /// Silences the unit, preventing it from casting spells for a duration.
    /// </summary>
    /// <param name="duration">The silence duration in seconds.</param>
    void Silence(float duration);

    /// <summary>
    /// Adds an item to the unit's inventory.
    /// </summary>
    /// <param name="itemId">The identifier of the item to add.</param>
    /// <returns>True if the item was added successfully, false otherwise.</returns>
    bool AddItem(string itemId);

    /// <summary>
    /// Removes an item from the unit's inventory.
    /// </summary>
    /// <param name="itemId">The identifier of the item to remove.</param>
    /// <returns>True if the item was found and removed, false otherwise.</returns>
    bool RemoveItem(string itemId);

    /// <summary>
    /// Checks if the unit has the specified item in its inventory.
    /// </summary>
    /// <param name="itemId">The item identifier to search for.</param>
    /// <returns>True if the item is present, false otherwise.</returns>
    bool HasItem(string itemId);

    /// <summary>
    /// Retrieves all items currently in the unit's inventory.
    /// </summary>
    /// <returns>A collection of item identifiers.</returns>
    IEnumerable<string> GetItems();

    /// <summary>
    /// Adds a buff/status effect to the unit.
    /// </summary>
    /// <param name="buffId">The identifier of the buff.</param>
    /// <param name="duration">The duration of the buff in seconds.</param>
    void AddBuff(string buffId, float duration);

    /// <summary>
    /// Removes a buff/status effect from the unit.
    /// </summary>
    /// <param name="buffId">The identifier of the buff to remove.</param>
    void RemoveBuff(string buffId);

    /// <summary>
    /// Checks if the unit currently has the specified buff.
    /// </summary>
    /// <param name="buffId">The identifier of the buff to check.</param>
    /// <returns>True if the buff is active, false otherwise.</returns>
    bool HasBuff(string buffId);

    /// <summary>
    /// Gets the list of active stat modifiers on the unit.
    /// </summary>
    /// <returns>A collection of modifier description strings.</returns>
    IEnumerable<string> GetModifiers();

    /// <summary>
    /// Associates custom data with a unique string key on this unit.
    /// </summary>
    /// <param name="key">The unique data key.</param>
    /// <param name="value">The data object to store.</param>
    void SetCustomData(string key, string value);

    /// <summary>
    /// Associates custom data with a unique string key on this unit.
    /// </summary>
    /// <param name="key">The unique data key.</param>
    /// <param name="value">The data object to store.</param>
    void SetCustomData(string key, object value) => SetCustomData(key, value?.ToString() ?? "");

    /// <summary>
    /// Retrieves custom data associated with a unique string key on this unit.
    /// </summary>
    /// <param name="key">The data key.</param>
    /// <returns>The stored data object, or null if the key does not exist.</returns>
    string? GetCustomData(string key);

    /// <summary>
    /// Removes custom data associated with a unique string key on this unit.
    /// </summary>
    /// <param name="key">The data key to remove.</param>
    /// <returns>True if the data was found and removed, false otherwise.</returns>
    bool RemoveCustomData(string key);

    /// <summary>
    /// Checks if this unit has custom data associated with a unique string key.
    /// </summary>
    /// <param name="key">The data key to check.</param>
    /// <returns>True if the data exists, false otherwise.</returns>
    bool HasCustomData(string key);

    /// <summary>
    /// Triggers a transient procedural mesh deformation or hit impulse on this unit.
    /// </summary>
    /// <param name="strength">The peak strength of the impulse deformation.</param>
    /// <param name="duration">The duration of the impulse oscillation in seconds.</param>
    /// <param name="frequency">The oscillation frequency in Hz.</param>
    void TriggerMeshImpulse(float strength = 1.0f, float duration = 0.5f, float frequency = 12.0f);
}
