using System;
using System.Collections.Generic;

public class TerrainSwatchProfileData
{
	public string SwatchName { get; set; } = "";
	public int DefaultPathingCode { get; set; } = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_BUILDABLE | EditableTerrain.PATHING_FLYING;
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public TerrainSwatchProfileData Clone()
	{
		var clone = new TerrainSwatchProfileData
		{
			SwatchName = this.SwatchName,
			DefaultPathingCode = this.DefaultPathingCode,
			DecalBombingRules = new List<ProceduralBombingDecalRule>(),
			VfxBombingRules = new List<ProceduralBombingVfxRule>()
		};
		if (this.DecalBombingRules != null)
		{
			foreach (var r in this.DecalBombingRules) clone.DecalBombingRules.Add(r.Clone());
		}
		if (this.VfxBombingRules != null)
		{
			foreach (var r in this.VfxBombingRules) clone.VfxBombingRules.Add(r.Clone());
		}
		return clone;
	}
}
