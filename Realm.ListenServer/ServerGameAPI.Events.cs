using System.Numerics;
using Realm.MapAPI;

namespace Realm.ListenServer;

/// <summary>
///     What a map can be told about as the match runs. Every one of these is raised by the authoritative simulation
///     itself, so a map reacts to what actually happened on the authority rather than to what a particular client
///     believed happened on its screen.
/// </summary>
public partial class ServerGameAPI
{
	/// <summary>Raised when a unit enters the world, whether the map placed it or a building trained it.</summary>
	public event Action<UnitId>? OnUnitCreated;

	/// <summary>Raised when a unit dies. The first unit is the one that died, the second is what killed it.</summary>
	public event Action<UnitId, UnitId>? OnUnitDied;

	/// <summary>Raised when a unit takes damage, with the amount that was applied.</summary>
	public event Action<UnitId, UnitId, float>? OnUnitDamaged;

	/// <summary>Raised when an ability a map scripted rather than authored is cast, with the place it was cast at.</summary>
	public event Action<UnitId, string, Vector3>? OnSpellCast;

	/// <summary>Raised when a player sends a chat message.</summary>
	public event Action<string, UnitId>? OnPlayerChatMessage;

	/// <summary>Raised when a player selects a unit.</summary>
	public event Action<UnitId>? OnUnitSelected;

	/// <summary>Raised when what a unit carries of a consumable changes.</summary>
	public event Action<UnitId, string>? OnConsumableChanged;

	/// <summary>Raised when a unit attacks another, before the damage is resolved.</summary>
	public event Action<UnitId, UnitId>? OnUnitAttacked;

	/// <summary>Raised when a unit picks a piece of ground loot up, with the unit (or an invalid unit when nothing took it).</summary>
	public event Action<LootItemId, UnitId>? OnLootItemCollected;

	/// <summary>Raised when a timer the map scheduled expires.</summary>
	public event Action<int>? OnTimerExpired;

	/// <summary>Raised when a unit enters a zone the map defined.</summary>
	public event Action<UnitId, int>? OnUnitEnterZone;

	/// <summary>Raised when a player leaves the match, whether they left or the map dropped them.</summary>
	public event Action<int>? OnPlayerLeft;

	/// <summary>Raised when an item is sold, with the item the unit sold.</summary>
	public event Action<UnitId, string>? OnItemSold;

	/// <summary>Raised when a building finishes being built.</summary>
	public event Action<UnitId>? OnConstructionFinished;

	/// <summary>Raised when a unit is given an order, with the order's name and where it points.</summary>
	public event Action<UnitId, string, Vector3>? OnUnitOrdered;

	/// <summary>Raised when a bot takes one of the decisions the map made for it.</summary>
	public event Action<int, string, string, Vector3, string>? OnBotCustomActionExecuted;
}
