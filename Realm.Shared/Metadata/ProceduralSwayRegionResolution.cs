using System.Collections.Generic;
using Realm.Shared.Terrain;

namespace Realm.Shared.Metadata;

public class ProceduralSwayRegionResolution
{
	public string RegionName { get; init; } = string.Empty;

	public ProceduralSwayRegionSource Source { get; init; } = ProceduralSwayRegionSource.PresetMask;

	public Vector3Data NormalizedMin { get; init; } = new(0.0f, 0.5f, 0.0f);

	public Vector3Data NormalizedMax { get; init; } = new(1.0f, 1.0f, 1.0f);

	public float Feather { get; init; } = 0.15f;

	public float MaskPower { get; init; } = 1.0f;

	public bool MaskInvert { get; init; } = false;

	public IReadOnlyList<string> MeshNamePatterns { get; init; } = new List<string>();

	public IReadOnlyList<string> MaterialNamePatterns { get; init; } = new List<string>();

	public bool HasRegionBox => Source != ProceduralSwayRegionSource.PresetMask;

	public bool UsesPartPatterns => MeshNamePatterns.Count > 0 || MaterialNamePatterns.Count > 0;
}
