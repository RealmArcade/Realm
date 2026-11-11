using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Resources;

/// <summary>
///     Stores the gathering state for a worker unit entity, including the resource type, the amount each swing takes
///     from the node, how long a swing lasts, what the worker carries and where it is taking it.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct Gatherer
{
	public int ResourceTypeStringId;
	public float CarriedAmount;
	public float MaxCapacity;
	public Entity TargetEntity;
	public byte ReturningToBaseByte;

	/// <summary>Gets or sets the amount one swing takes from the node, or zero to take what the node advertises.</summary>
	public float SwingAmount;

	/// <summary>Gets or sets the seconds one swing lasts, or zero to swing once a second.</summary>
	public float SwingSeconds;

	/// <summary>Gets or sets the seconds accumulated toward the swing the worker is taking.</summary>
	public float SwingProgress;

	/// <summary>Gets or sets the fraction of each delivered swing the map keeps, or zero to pay the whole swing.</summary>
	public float GatherTaxRate;

	/// <summary>Gets or sets the fraction of a resource the worker has earned but not yet been paid, so a taxed delivery stays exact over a run.</summary>
	public float PendingIncomeRemainder;

	/// <summary>Gets or sets the item the node yields, which is what the worker carries home and pays back.</summary>
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
		CarriedAmount = 0f;
		MaxCapacity = 15f;
		TargetEntity = targetEntity;
		ReturningToBaseByte = 0;
		SwingAmount = 0f;
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
