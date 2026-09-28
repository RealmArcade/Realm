using System;
using System.Collections.Generic;
using Godot;
using Realm.Ecs.Components.Terrain;
using Realm.Godot.VFX;

public class ProceduralBombingDecalRule
{
	public string DecalId { get; set; } = "";
	public float Density { get; set; } = 0.5f;
	public float MinScale { get; set; } = 0.2f;
	public float MaxScale { get; set; } = 0.5f;
	public float MinRotationDeg { get; set; } = 0.0f;
	public float MaxRotationDeg { get; set; } = 360.0f;
	public string TintHex { get; set; } = "#FFFFFF";

	public ProceduralBombingDecalRule Clone()
	{
		return new ProceduralBombingDecalRule
		{
			DecalId = this.DecalId,
			Density = this.Density,
			MinScale = this.MinScale,
			MaxScale = this.MaxScale,
			MinRotationDeg = this.MinRotationDeg,
			MaxRotationDeg = this.MaxRotationDeg,
			TintHex = this.TintHex
		};
	}
}

public class ProceduralBombingVfxRule
{
	public string VfxId { get; set; } = "";
	public float Density { get; set; } = 0.5f;
	public float MinScale { get; set; } = 0.2f;
	public float MaxScale { get; set; } = 0.5f;
	public float NormalOffset { get; set; } = 0.0f;
	public VfxAttachmentConfig Config { get; set; }

	public ProceduralBombingVfxRule Clone()
	{
		return new ProceduralBombingVfxRule
		{
			VfxId = this.VfxId,
			Density = this.Density,
			MinScale = this.MinScale,
			MaxScale = this.MaxScale,
			NormalOffset = this.NormalOffset,
			Config = this.Config
		};
	}
}

public class WaterProfileSaveData
{
	public string Id { get; set; } = "water_default";
	public string Name { get; set; } = "Water";
	public byte ProfileIndex { get; set; } = 0;
	public WaterType WaterType { get; set; } = WaterType.Shallow;
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
	public float DetailUvScaleX { get; set; } = 1.0f;
	public float DetailUvScaleY { get; set; } = 1.0f;
	public float DetailUvScrollX { get; set; } = 0.0f;
	public float DetailUvScrollY { get; set; } = 0.0f;
	public float DetailAlpha { get; set; } = 0.5f;
	public int DetailBlendMode { get; set; } = 0;
	public int DefaultPathingCode { get; set; } = EditableTerrain.PATHING_SHALLOW_WATER | EditableTerrain.PATHING_FLYING;
	public List<ProceduralBombingDecalRule> DecalBombingRules { get; set; } = new();
	public List<ProceduralBombingVfxRule> VfxBombingRules { get; set; } = new();

	public WaterProfileSaveData Clone()
	{
		var clone = new WaterProfileSaveData
		{
			Id = this.Id,
			Name = this.Name,
			ProfileIndex = this.ProfileIndex,
			WaterType = this.WaterType,
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
			DetailUvScaleX = this.DetailUvScaleX,
			DetailUvScaleY = this.DetailUvScaleY,
			DetailUvScrollX = this.DetailUvScrollX,
			DetailUvScrollY = this.DetailUvScrollY,
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
				WaterType = WaterType.Shallow,
				ShallowColorHex = "#0D4D618C",
				DeepColorHex = "#083340B3",
				FoamColorHex = "#D9F2FFD9",
				MaxDepth = 1.5f,
				FoamDepth = 0.5f,
				WaveSpeed = 1.2f,
				WaveStrength = 0.06f,
				RefractionStrength = 0.2f,
				CausticStrength = 0.4f,
				DefaultPathingCode = EditableTerrain.PATHING_SHALLOW_WATER | EditableTerrain.PATHING_FLYING
			},
			new WaterProfileSaveData
			{
				Id = "water_deep",
				Name = "Deep Ocean",
				ProfileIndex = 1,
				WaterType = WaterType.Deep,
				ShallowColorHex = "#051F47B3",
				DeepColorHex = "#01081FFC",
				FoamColorHex = "#D9F2FFD9",
				MaxDepth = 2.5f,
				FoamDepth = 0.6f,
				WaveSpeed = 1.4f,
				WaveStrength = 0.08f,
				RefractionStrength = 0.3f,
				CausticStrength = 0.2f,
				DefaultPathingCode = EditableTerrain.PATHING_DEEP_WATER | EditableTerrain.PATHING_FLYING
			},
			new WaterProfileSaveData
			{
				Id = "liquid_lava",
				Name = "Molten Lava",
				ProfileIndex = 2,
				WaterType = WaterType.Deep,
				ShallowColorHex = "#E64C00F5",
				DeepColorHex = "#800A00FF",
				FoamColorHex = "#FFE680F0",
				MaxDepth = 1.0f,
				FoamDepth = 0.4f,
				WaveSpeed = 0.6f,
				WaveStrength = 0.04f,
				EmissionColorHex = "#FF3300FF",
				EmissionBoost = 4.0f,
				CoreColorHex = "#FFFFB3FF",
				CoreThreshold = 0.7f,
				SubsurfaceColorHex = "#FF6600FF",
				SubsurfaceStrength = 1.5f,
				DefaultPathingCode = EditableTerrain.PATHING_FLYING
			},
			new WaterProfileSaveData
			{
				Id = "liquid_acid",
				Name = "Toxic Acid",
				ProfileIndex = 3,
				WaterType = WaterType.Shallow,
				ShallowColorHex = "#33FF33B3",
				DeepColorHex = "#0D660DFA",
				FoamColorHex = "#B3FFB3F0",
				MaxDepth = 1.2f,
				FoamDepth = 0.5f,
				WaveSpeed = 1.5f,
				WaveStrength = 0.05f,
				EmissionColorHex = "#00FF00FF",
				EmissionBoost = 1.5f,
				RefractionStrength = 0.4f,
				DefaultPathingCode = EditableTerrain.PATHING_SHALLOW_WATER | EditableTerrain.PATHING_FLYING
			},
			new WaterProfileSaveData
			{
				Id = "liquid_poison",
				Name = "Poison Swamp",
				ProfileIndex = 4,
				WaterType = WaterType.Shallow,
				ShallowColorHex = "#5E1985CC",
				DeepColorHex = "#2B0542FC",
				FoamColorHex = "#B870E6D9",
				MaxDepth = 1.8f,
				FoamDepth = 0.6f,
				WaveSpeed = 0.8f,
				WaveStrength = 0.04f,
				EmissionColorHex = "#660080FF",
				EmissionBoost = 0.8f,
				DefaultPathingCode = EditableTerrain.PATHING_SHALLOW_WATER | EditableTerrain.PATHING_FLYING
			},
			new WaterProfileSaveData
			{
				Id = "liquid_geothermal",
				Name = "Geothermal Spring",
				ProfileIndex = 5,
				WaterType = WaterType.Shallow,
				ShallowColorHex = "#1AC6D9A6",
				DeepColorHex = "#0F6E7AFA",
				FoamColorHex = "#E0FFFFE6",
				MaxDepth = 1.5f,
				FoamDepth = 0.5f,
				WaveSpeed = 1.1f,
				WaveStrength = 0.05f,
				EmissionColorHex = "#00E5FFFF",
				EmissionBoost = 0.5f,
				RefractionStrength = 0.25f,
				CausticStrength = 0.5f,
				DefaultPathingCode = EditableTerrain.PATHING_SHALLOW_WATER | EditableTerrain.PATHING_FLYING
			}
		};
	}
}
