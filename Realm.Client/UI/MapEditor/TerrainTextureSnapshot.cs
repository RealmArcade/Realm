using Godot;
using System.Collections.Generic;

namespace Realm.Client.UI.MapEditor;

public class TerrainTextureSnapshot
{
	public float Brightness { get; set; } = 1.0f;
	public Color Tint { get; set; } = Colors.White;
	public float HeightScale { get; set; } = 1.0f;
	public float HeightOffset { get; set; } = 0.0f;
	public float CrevicePower { get; set; } = 1.0f;
	public float NormalScale { get; set; } = 1.0f;
	public float RoughnessScale { get; set; } = 1.0f;
	public string TileMode { get; set; } = "Stochastic";
	public float UvScale { get; set; } = 1.0f;
	public float StochasticTileSize { get; set; } = 1.0f;
	public float CrossFade { get; set; } = 0.0f;
	public int DefaultPathingCode { get; set; } = Realm.Client.EditableTerrain.PATHING_GROUND | Realm.Client.EditableTerrain.PATHING_BUILDABLE | Realm.Client.EditableTerrain.PATHING_FLYING;
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public TerrainTextureSnapshot Clone()
	{
		var clone = new TerrainTextureSnapshot
		{
			Brightness = this.Brightness,
			Tint = this.Tint,
			HeightScale = this.HeightScale,
			HeightOffset = this.HeightOffset,
			CrevicePower = this.CrevicePower,
			NormalScale = this.NormalScale,
			RoughnessScale = this.RoughnessScale,
			TileMode = this.TileMode,
			UvScale = this.UvScale,
			StochasticTileSize = this.StochasticTileSize,
			CrossFade = this.CrossFade,
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