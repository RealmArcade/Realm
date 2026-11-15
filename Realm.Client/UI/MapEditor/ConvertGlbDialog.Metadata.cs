// Writing the converted asset's metadata: the scale and Y offset default per sub-category, then the
// per-kind field sets (unit, building, resource, prop, item).

using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Shared;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;
using Realm.Client.Utils;


namespace Realm.Client.UI.MapEditor;

public partial class ConvertGlbDialog
{
	private float GetDefaultScaleForSubCategory(string subCategory)
	{
		return subCategory switch
		{
			"resources" => 2.75f,
			"buildings" => 1.5f,
			"props" => 1.25f,
			"units" => 1.0f,
			"attachments" or "items" => 1.0f,
			_ => 1.0f
		};
	}

	private void CalculateAndSaveMetadata(string resultPath, string fileName, string subCategory, float defaultScale)
	{
		var (minY, autoYOffset) = ModelCache.CalculateModelBounds(resultPath, defaultScale);
		string wsPath = GameContext.EditorService._editingDirectory;
		string unitId = Path.GetFileNameWithoutExtension(fileName);

		GameContext.MetadataService.UpdateMetadata(wsPath, meta =>
		{
			UpdateSpecificMetadata(meta, subCategory, unitId, fileName, defaultScale, autoYOffset, minY);
		});

		GameContext.MetadataService.CleanMetadata(wsPath);
		GameContext.Host?.SetModelYOffset(fileName, autoYOffset);
		GameContext.Host?.SetModelScale(fileName, defaultScale);
		GameContext.Host?.FlushModelYOffsetSave();
		GameContext.Host?.LoadUnitMetadata(wsPath);
	}

	private void UpdateSpecificMetadata(MapMetadata meta, string subCategory, string unitId, string fileName, float defaultScale, float autoYOffset, float minY)
	{
		switch (subCategory)
		{
			case "units" or "characters":
				UpdateUnitMetadata(meta, unitId, fileName, defaultScale, autoYOffset, minY);
				break;
			case "buildings":
				UpdateBuildingMetadata(meta, unitId, fileName, defaultScale, autoYOffset, minY);
				break;
			case "resources":
				UpdateResourceMetadata(meta, unitId, fileName, defaultScale, autoYOffset, minY);
				break;
			case "props":
				UpdatePropMetadata(meta, unitId, fileName, defaultScale, autoYOffset, minY);
				break;
			case "attachments" or "items":
				UpdateItemMetadata(meta, unitId);
				break;
		}
	}

	private void UpdateUnitMetadata(MapMetadata meta, string unitId, string fileName, float defaultScale, float autoYOffset, float minY)
	{
		string unitTemplateId = TemplateIDHelper.NormalizeTemplateID("unit", unitId);
		bool updatedU = meta.UpdateUnit(unitTemplateId, u =>
		{
			if (float.IsFinite(minY) && Math.Abs(minY) > 0.0001f) u.YOffset = ModelCache.RestingOffsetFor(minY, u.Scale);
			return u;
		});
		if (!updatedU)
		{
			meta.AddOrUpdateUnit(new UnitMetadata
			{
				TemplateID = unitTemplateId,
				Name = unitId,
				Description = "",
				ModelPath = fileName,
				Scale = defaultScale,
				YOffset = autoYOffset,
				PathingType = 9,
				DespillPlayerColor = false,
				NormalizeLuminance = true
			});
		}
	}

	private void UpdateBuildingMetadata(MapMetadata meta, string unitId, string fileName, float defaultScale, float autoYOffset, float minY)
	{
		string buildingTemplateId = TemplateIDHelper.NormalizeTemplateID("building", unitId);
		bool updatedB = meta.UpdateBuilding(buildingTemplateId, b =>
		{
			if (float.IsFinite(minY) && Math.Abs(minY) > 0.0001f) b.YOffset = ModelCache.RestingOffsetFor(minY, b.Scale);
			return b;
		});
		if (!updatedB)
		{
			meta.AddOrUpdateBuilding(new UnitMetadata
			{
				TemplateID = buildingTemplateId,
				Name = unitId,
				Description = "",
				ModelPath = fileName,
				Scale = defaultScale,
				YOffset = autoYOffset,
				PathingType = 32,
				DespillPlayerColor = false,
				NormalizeLuminance = true
			});
		}
	}

	private void UpdateResourceMetadata(MapMetadata meta, string unitId, string fileName, float defaultScale, float autoYOffset, float minY)
	{
		string resourceTemplateId = TemplateIDHelper.NormalizeTemplateID("resource", unitId);
		bool updatedR = meta.UpdateResource(resourceTemplateId, r =>
		{
			if (float.IsFinite(minY) && Math.Abs(minY) > 0.0001f) r.YOffset = ModelCache.RestingOffsetFor(minY, r.Scale);
			return r;
		});
		if (!updatedR)
		{
			meta.AddOrUpdateResource(new ResourceMetadata
			{
				TemplateID = resourceTemplateId,
				Name = unitId,
				Description = "",
				ModelPath = fileName,
				Scale = defaultScale,
				YOffset = autoYOffset,
				PathingType = 255,
				DespillPlayerColor = false,
				NormalizeLuminance = true,
				IgnorePlayerColor = true
			});
		}
	}

	private void UpdatePropMetadata(MapMetadata meta, string unitId, string fileName, float defaultScale, float autoYOffset, float minY)
	{
		string propTemplateId = TemplateIDHelper.NormalizeTemplateID("prop", unitId);
		bool updatedP = meta.UpdateProp(propTemplateId, p =>
		{
			if (float.IsFinite(minY) && Math.Abs(minY) > 0.0001f) p.YOffset = ModelCache.RestingOffsetFor(minY, p.Scale);
			return p;
		});
		if (!updatedP)
		{
			meta.AddOrUpdateProp(new PropMetadata
			{
				TemplateID = propTemplateId,
				Name = unitId,
				Description = "",
				ModelPath = fileName,
				Scale = defaultScale,
				YOffset = autoYOffset,
				PathingType = 255,
				DespillPlayerColor = false,
				NormalizeLuminance = true,
				IgnorePlayerColor = true
			});
		}
	}

	private void UpdateItemMetadata(MapMetadata meta, string unitId)
	{
		string itemTemplateId = TemplateIDHelper.NormalizeTemplateID("item", unitId);
		bool updatedI = meta.UpdateItem(itemTemplateId, i => i);
		if (!updatedI)
		{
			meta.AddOrUpdateItem(new ItemMetadata
			{
				TemplateID = itemTemplateId,
				Name = unitId,
				Description = "",
				ItemClass = "consumable",
				CanDrop = true
			});
		}
	}
}