using System;
using System.Runtime.InteropServices;
using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Marks a node whose health is its remaining resource, and states which item each point of damage taken from it puts
///     into the attacker's inventory.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Harvestable
{
	public int ResourceTypeStringId;
	public int ItemStringId;
	public float YieldPerDamage;

	public Harvestable(string resourceType, string itemId, float yieldPerDamage = 1f)
	{
		ResourceTypeStringId = StringRegistry.GetOrAdd(resourceType);
		ItemStringId = StringRegistry.GetOrAdd(itemId);
		YieldPerDamage = yieldPerDamage > 0f ? yieldPerDamage : 1f;
	}

	public string ResourceType
	{
		get => StringRegistry.Get(ResourceTypeStringId);
		set => ResourceTypeStringId = StringRegistry.GetOrAdd(value);
	}

	public string ItemId
	{
		get => StringRegistry.Get(ItemStringId);
		set => ItemStringId = StringRegistry.GetOrAdd(value);
	}
}
