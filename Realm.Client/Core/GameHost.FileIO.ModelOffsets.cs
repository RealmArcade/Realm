using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Realm.Ecs;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Shared.Maps;
using Realm.Shared.Terrain;
using Realm.Shared.Textures;
using Vector3 = System.Numerics.Vector3;

namespace Realm.Client.Core;


public partial class GameHost
{
	public void FlushModelYOffsetSave()
	{
		if (GameContext.EditorService.ConsumeModelOffsetSavePending())
		{
			SaveModelYOffsetsToMetadataJson();
		}
	}

	public void FlushModelCollisionCircleSave()
	{
		FlushModelYOffsetSave();
	}

	public void LoadModelYOffsetsFromMetadataJson(string? directory = null)
	{
		try
		{
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : GameContext.MapStorageService.CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir))
			{
				mapDir = GameContext.EditorService._editingDirectory;
			}

			LoadUnitMetadata(mapDir);
			var metaService = GameContext.MetadataService;
			var metadata = metaService.LoadMetadata(mapDir);

			GameContext.ModelOverrideService.LoadOverridesFromMetadata(metadata, GameContext.EditorService.NormalizeModelAssetKey);

			UpdateAllMaterialOverrides();
		}
		catch (Exception ex)
		{
			GameLog.LogError($"Failed to load metadata overrides from JSON: {ex.Message}");
		}
	}










	public void SaveModelYOffsetsToMetadataJson(string? directory = null)
	{
		try
		{
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : GameContext.MapStorageService.CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir)) mapDir = GameContext.EditorService._editingDirectory;

			var metaService = GameContext.MetadataService;
			metaService.UpdateMetadata(mapDir, UpdateMetadataModelOverrides);
		}
		catch (Exception ex)
		{
			GameLog.LogError($"SaveModelYOffsetsToMetadataJson error: {ex.Message}");
		}
	}
}
