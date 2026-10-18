using System.Runtime.InteropServices;

namespace Realm.Ecs.Components.Combat;

/// <summary>
/// Defines sight range, acquisition range, stealth detection, and target filtering intelligence for an entity.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct TargetIntel
{
	public float SightRange;
	public float AcquisitionRange;
	public int DetectionTypeId;
	public int StealthStateId;
	public int TargetAllowedTypesId;

	public TargetIntel(
		float sightRange = 15f,
		float acquisitionRange = 15f,
		string detectionType = "normal",
		string stealthState = "none",
		string targetAllowedTypes = "ground,air")
	{
		SightRange = sightRange;
		AcquisitionRange = acquisitionRange;
		DetectionTypeId = StringRegistry.GetOrAdd(detectionType);
		StealthStateId = StringRegistry.GetOrAdd(stealthState);
		TargetAllowedTypesId = StringRegistry.GetOrAdd(targetAllowedTypes);
	}

	public string DetectionType
	{
		get => StringRegistry.Get(DetectionTypeId);
		set => DetectionTypeId = StringRegistry.GetOrAdd(value);
	}

	public string StealthState
	{
		get => StringRegistry.Get(StealthStateId);
		set => StealthStateId = StringRegistry.GetOrAdd(value);
	}

	public string TargetAllowedTypes
	{
		get => StringRegistry.Get(TargetAllowedTypesId);
		set => TargetAllowedTypesId = StringRegistry.GetOrAdd(value);
	}
}
