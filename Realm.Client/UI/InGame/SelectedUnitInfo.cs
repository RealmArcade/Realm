using System;
using System.Collections.Generic;
using Realm.Ecs;

namespace Realm.Client.UI.InGame;

/// <summary>
/// One of the map's consumables a unit is carrying, as the authority described it. Nothing about it is decided on the
/// screen: the unit's own carried state arrives as replicated state, so a player sees the elixir an ally bound to a unit
/// exactly as the authority recorded it.
/// </summary>
public readonly record struct CarriedConsumableInfo(string DefinitionId, string SlotGroup);

public class SelectedUnitInfo
{
	public Entity Entity { get; set; }
	public string UnitId { get; set; }
	public string Name { get; set; }
	public float Health { get; set; }
	public float MaxHealth { get; set; }
	public float Damage { get; set; }
	public float Range { get; set; }
	public float Armor { get; set; }
	public float Speed { get; set; }
	public float Dps { get; set; }
	public int Player { get; set; }
	public bool IsEnemy { get; set; }
	public bool IsBuilding { get; set; }
	public bool IsUnderConstruction { get; set; }
	public string StateText { get; set; }
	public string Description { get; set; }
	public List<string> Abilities { get; set; } = new();
	public int Potions { get; set; }
	public Dictionary<string, int> InventoryItems { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public List<CarriedConsumableInfo> CarriedConsumables { get; set; } = new();
	public bool HasInventory { get; set; }

	public bool HasProduction { get; set; }
	public string ProductionTitle { get; set; }
	public float ProductionProgress { get; set; }
	public float ProductionMaxProgress { get; set; }
	public List<string> ProductionQueue { get; set; } = new();
}