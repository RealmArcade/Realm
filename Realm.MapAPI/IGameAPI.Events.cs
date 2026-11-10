// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Triggered when a unit is spawned in the game world.
	/// </summary>
	event Action<UnitId>? OnUnitCreated;

	/// <summary>
	/// Triggered when a unit dies. Passes the dying unit and the killer unit if available.
	/// </summary>
	event Action<UnitId, UnitId>? OnUnitDied;

	/// <summary>
	/// Triggered when a unit takes damage. Passes the victim unit, the attacker unit, and the damage amount.
	/// </summary>
	event Action<UnitId, UnitId, float>? OnUnitDamaged;

	/// <summary>
	/// Triggered when a unit is hit, passing the damage the blow carried before the victim's resistances reduced it.
	/// </summary>
	/// <remarks>
	/// A rule that weighs how much a side is contributing to a fight reads this rather than
	/// <see cref="OnUnitDamaged" />, because damage that lands is what the victim's armour and magic resistance decide
	/// and would let a heavily armoured target absorb participation. Both events describe the same blow, this one first.
	/// </remarks>
	event Action<UnitId, UnitId, float>? OnUnitDamagedBeforeMitigation;

	/// <summary>
	/// Triggered when a unit casts or channels a spell. Passes the caster unit, the spell ID, and the target position.
	/// </summary>
	event Action<UnitId, string, Vector3>? OnSpellCast;

	/// <summary>
	/// Triggered when a projectile an ability fired as its own visual reaches the point the ability was cast at, which
	/// is when the ability's effect belongs on the ground rather than on the click. Only abilities whose template names
	/// a projectile raise this, so the shots of ordinary attacks cost nothing.
	/// </summary>
	/// <remarks>
	/// The arguments are the caster, the ability whose projectile landed, the point the ability was cast at, and the
	/// player that owned the caster when it was cast. The caster may have moved or died during the flight.
	/// </remarks>
	event Action<UnitId, string, Vector3, int>? OnSpellImpact;

	/// <summary>
	/// Triggered when a player types a chat message. Passes the message, the first selected unit if any, and the slot of
	/// the player who typed it, which is the slot to judge a console command by even when that player had nothing
	/// selected.
	/// </summary>
	event Action<string, UnitId, int>? OnPlayerChatMessage;

	/// <summary>
	/// Triggered when the player selects a unit.
	/// </summary>
	event Action<UnitId>? OnUnitSelected;

	/// <summary>
	/// Triggered after a consumable is bound to a unit or purged from one. The engine sells the
	/// purchase; this event is where the map switches on the behaviour the consumable grants.
	/// </summary>
	/// <remarks>
	/// The arguments are the unit that now carries the consumable (or just lost it) and the attached consumable's
	/// template id, which is empty when the slot was cleared.
	/// </remarks>
	event Action<UnitId, string>? OnConsumableChanged;

	/// <summary>
	/// Retrieves the consumable attached to a unit in the given slot group.
	/// </summary>
	/// <param name="unit">The unit to inspect.</param>
	/// <param name="slotGroup">The authored slot group, for example "elixir".</param>
	/// <returns>The consumable's template id, or an empty string when the unit carries none.</returns>
	string GetUnitConsumable(UnitId unit, string slotGroup)
	{
		return string.Empty;
	}

	/// <summary>
	/// Triggered when a unit attacks another unit. Passes the attacker unit and the target unit.
	/// </summary>
	event Action<UnitId, UnitId>? OnUnitAttacked;

	/// <summary>
	/// Triggered when a ground loot item is collected by a unit. Passes the collected loot item and the collecting unit.
	/// </summary>
	event Action<LootItemId, UnitId>? OnLootItemCollected;
}
