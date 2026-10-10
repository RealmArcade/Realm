namespace Realm.Shared.Metadata;

public class WaterProfileSaveData
{
	public string Id { get; set; } = "water_default";
	public string Name { get; set; } = "Water";
	public byte ProfileIndex { get; set; } = 0;
	public string ShallowColorHex { get; set; } = "#0D4D618C";
	public string DeepColorHex { get; set; } = "#030F24FA";
	public string FoamColorHex { get; set; } = "#D9F2FFD9";
	public float MaxDepth { get; set; } = 2.0f;
	public float FoamDepth { get; set; } = 0.6f;
	public float WaveSpeed { get; set; } = 1.2f;
	public float WaveStrength { get; set; } = 0.06f;
	public bool UseNormalTexture { get; set; } = false;
	public string NormalTexturePath { get; set; } = "";
	public float NormalScale { get; set; } = 1.0f;
	public float FlowDirectionX { get; set; } = 1.0f;
	public float FlowDirectionY { get; set; } = 0.0f;
	public float FlowSpeed { get; set; } = 0.5f;
	public bool UseFlowMap { get; set; } = false;
	public string FlowMapPath { get; set; } = "";
	public float RefractionStrength { get; set; } = 0.0f;
	public float CausticStrength { get; set; } = 0.0f;
	public float CausticScale { get; set; } = 1.0f;
	public float CausticSpeed { get; set; } = 1.0f;
	public string EmissionColorHex { get; set; } = "#000000FF";
	public float EmissionBoost { get; set; } = 0.0f;
	public string CoreColorHex { get; set; } = "#FFE680FF";
	public float CoreThreshold { get; set; } = 0.8f;
	public string SubsurfaceColorHex { get; set; } = "#000000FF";
	public float SubsurfaceStrength { get; set; } = 0.0f;
	public bool UseDetailTexture { get; set; } = false;
	public string DetailTexturePath { get; set; } = "";
	public string DetailTileMode { get; set; } = "Stochastic";
	public float DetailUvScaleX { get; set; } = 1.0f;
	public float DetailUvScaleY { get; set; } = 1.0f;
	public float DetailUvScrollX { get; set; } = 0.0f;
	public float DetailUvScrollY { get; set; } = 0.0f;
	public float DetailStochasticTileSize { get; set; } = 1.0f;
	public float DetailCrossFade { get; set; } = 0.0f;
	public float DetailAlpha { get; set; } = 0.5f;
	public int DetailBlendMode { get; set; } = 0;
	public int DefaultPathingCode { get; set; } = 1 | 4; // ShallowWater | Flying
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public WaterProfileSaveData Clone()
	{
		var clone = new WaterProfileSaveData
		{
			Id = this.Id,
			Name = this.Name,
			ProfileIndex = this.ProfileIndex,
			ShallowColorHex = this.ShallowColorHex,
			DeepColorHex = this.DeepColorHex,
			FoamColorHex = this.FoamColorHex,
			MaxDepth = this.MaxDepth,
			FoamDepth = this.FoamDepth,
			WaveSpeed = this.WaveSpeed,
			WaveStrength = this.WaveStrength,
			UseNormalTexture = this.UseNormalTexture,
			NormalTexturePath = this.NormalTexturePath,
			NormalScale = this.NormalScale,
			FlowDirectionX = this.FlowDirectionX,
			FlowDirectionY = this.FlowDirectionY,
			FlowSpeed = this.FlowSpeed,
			UseFlowMap = this.UseFlowMap,
			FlowMapPath = this.FlowMapPath,
			RefractionStrength = this.RefractionStrength,
			CausticStrength = this.CausticStrength,
			CausticScale = this.CausticScale,
			CausticSpeed = this.CausticSpeed,
			EmissionColorHex = this.EmissionColorHex,
			EmissionBoost = this.EmissionBoost,
			CoreColorHex = this.CoreColorHex,
			CoreThreshold = this.CoreThreshold,
			SubsurfaceColorHex = this.SubsurfaceColorHex,
			SubsurfaceStrength = this.SubsurfaceStrength,
			UseDetailTexture = this.UseDetailTexture,
			DetailTexturePath = this.DetailTexturePath,
			DetailTileMode = this.DetailTileMode,
			DetailUvScaleX = this.DetailUvScaleX,
			DetailUvScaleY = this.DetailUvScaleY,
			DetailUvScrollX = this.DetailUvScrollX,
			DetailUvScrollY = this.DetailUvScrollY,
			DetailStochasticTileSize = this.DetailStochasticTileSize,
			DetailCrossFade = this.DetailCrossFade,
			DetailAlpha = this.DetailAlpha,
			DetailBlendMode = this.DetailBlendMode,
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

	public static List<WaterProfileSaveData> CreateDefaultProfiles()
	{
		return new List<WaterProfileSaveData>
		{
			new WaterProfileSaveData
			{
				Id = "water_shallow",
				Name = "Shallow Water",
				ProfileIndex = 0,
				ShallowColorHex = "#4AD4E655",
				DeepColorHex = "#0A5C8AE0",
				FoamColorHex = "#F0FBFFC0",
				MaxDepth = 1.8f,
				FoamDepth = 0.20f,
				WaveSpeed = 1.2f,
				WaveStrength = 0.04f,
				NormalScale = 0.4f,
				RefractionStrength = 0.20f,
				CausticStrength = 0.50f,
				CausticScale = 1.5f,
				CausticSpeed = 1.2f,
				DefaultPathingCode = 1 | 4
			},
			new WaterProfileSaveData
			{
				Id = "water_deep",
				Name = "Deep Ocean",
				ProfileIndex = 1,
				ShallowColorHex = "#082B5ECC",
				DeepColorHex = "#010614FA",
				FoamColorHex = "#D0EBFFB0",
				MaxDepth = 3.0f,
				FoamDepth = 0.22f,
				WaveSpeed = 1.0f,
				WaveStrength = 0.06f,
				NormalScale = 0.1f,
				RefractionStrength = 0.08f,
				CausticStrength = 0.20f,
				CausticScale = 0.8f,
				CausticSpeed = 0.8f,
				DefaultPathingCode = 2 | 4
			}
		};
	}
}