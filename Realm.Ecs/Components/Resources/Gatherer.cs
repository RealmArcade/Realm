using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Stores the gathering state of a unit working a harvestable node: the node it works, the item that node yields,
///     how far into its next swing it is, and whether it is taking what it has acquired to a drop-off to sell.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Gatherer
{
	public int ResourceTypeStringId;
	public Entity TargetEntity;
	public byte ReturningToBaseByte;

	/// <summary>Gets or sets the seconds one swing lasts, or zero to swing at the unit's own attack interval.</summary>
	public float SwingSeconds;

	/// <summary>Gets or sets the seconds accumulated toward the swing the worker is taking.</summary>
	public float SwingProgress;

	/// <summary>Gets or sets the fraction of each piece sold the map keeps, or zero to pay the whole sale.</summary>
	public float GatherTaxRate;

	/// <summary>Gets or sets the fraction of a resource the worker has earned but not yet been paid, so a taxed sale stays exact over a run.</summary>
	public float PendingIncomeRemainder;

	/// <summary>Gets or sets the item the node yields, which is what the worker carries in its inventory and sells at a drop-off.</summary>
	public int ItemStringId;

	/// <summary>Gets or sets the fraction of a point of the node's health the last hit earned but had not yet filled.</summary>
	public float HarvestRemainder;

	public bool ReturningToBase
	{
		get => ReturningToBaseByte != 0;
		set => ReturningToBaseByte = value ? (byte)1 : (byte)0;
	}

	public Gatherer(string type, Entity targetEntity)
	{
		ResourceTypeStringId = StringRegistry.GetOrAdd(type);
		TargetEntity = targetEntity;
		ReturningToBaseByte = 0;
		SwingSeconds = 0f;
		SwingProgress = 0f;
		GatherTaxRate = 0f;
		PendingIncomeRemainder = 0f;
		ItemStringId = 0;
		HarvestRemainder = 0f;
	}

	public string ResourceType
	{
		get => StringRegistry.Get(ResourceTypeStringId);
		set => ResourceTypeStringId = StringRegistry.GetOrAdd(value);
	}
}
