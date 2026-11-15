using System;
using System.Collections.Generic;
using Realm.Ecs;

namespace Realm.Client.UI.InGame;

public readonly record struct CarriedConsumableInfo(string DefinitionId, string SlotGroup);

/// <summary>One filled inventory slot: the item it holds and how many pieces of it that slot carries.</summary>
public readonly record struct CarriedItemInfo(string ItemId, int Count);

public class SelectedUnitInfo
{
	public Entity Entity { get; set; }
	public string UnitId { get; set; } = null!;
	public string Name { get; set; } = null!;
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
	public string? StateText { get; set; }
	public string? Description { get; set; }
	public List<string> Abilities { get; set; } = new();
	public int AbilityListHandle { get; set; }
	public int Potions { get; set; }
	public List<CarriedItemInfo> InventoryItems { get; set; } = new();
	public List<CarriedConsumableInfo> CarriedConsumables { get; set; } = new();
	public bool HasInventory { get; set; }

	public bool HasProduction { get; set; }
	public string? ProductionTitle { get; set; }
	public float ProductionProgress { get; set; }
	public float ProductionMaxProgress { get; set; }
	public List<string> ProductionQueue { get; set; } = new();
}