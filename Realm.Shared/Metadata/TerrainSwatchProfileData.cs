using Realm.Ecs.Components.Terrain;

namespace Realm.Shared.Metadata;

public class TerrainSwatchProfileData
{
	public int DefaultPathingCode { get; set; } = TerrainPathing.DefaultPathingCode_Ground;
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public TerrainSwatchProfileData Clone()
	{
		var clone = new TerrainSwatchProfileData
		{
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