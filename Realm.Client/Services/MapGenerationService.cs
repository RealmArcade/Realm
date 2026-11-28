using System;
using Godot;
using Realm.Client.Services.MapGeneration;
using Realm.EditorAPI;

namespace Realm.Client.Services;

public class MapGenerationService
{
	private static readonly string[] TreeKeywords = { "tree", "palm", "conifer", "oak", "pine", "bush" };
	private static readonly string[] DecorationKeywords = { "rock", "pillar", "flag", "boulder", "statue", "ruin" };

	private ProceduralMapGenerator? _generator;
	private MapStateSnapshot? _previewSnapshot;

	public bool IsPreviewActive => _generator != null;

	public bool IsRunning => _generator is { IsComplete: false, HasFailed: false };

	public MapGenerationProgress Progress { get; private set; } = new(MapGenerationStage.Preparing, 0f, false, false, false, null);

	public string LastTerrainImageFolder { get; set; } = string.Empty;

	public bool StartPreview(string seedText, MapGenerationParameters parameters)
	{
		DiscardRun();
		Progress = new MapGenerationProgress(MapGenerationStage.Preparing, 0f, true, false, false, null);

		try
		{
			IEditorAPI editorApi = GameContext.IEditorAPI;
			TerrainGridInfo gridInfo = editorApi.GetTerrainGridInfo();
			if (gridInfo.Width <= 0 || gridInfo.Depth <= 0)
			{
				return FailRegistration("The editor has no terrain grid to generate into.");
			}

			_previewSnapshot = MapStateSnapshot.CreateSnapshot();
			if (_previewSnapshot == null)
			{
				return FailRegistration("The editor has no map open to generate into.");
			}

			GameContext.Host?.ClearPlacedEntitiesExternal();
			_generator = CreateGenerator(editorApi, gridInfo, seedText, parameters);
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapGenerationService] Failed to start procedural generation: {exception}");
			return FailRegistration(exception.Message);
		}
	}

	public bool UpdateParameters(MapGenerationParameters parameters)
	{
		return _generator != null && _generator.SetParameters(parameters);
	}

	public bool UpdateSeed(string seedText)
	{
		if (_generator == null || string.Equals(_generator.Seed, seedText ?? string.Empty, StringComparison.Ordinal))
		{
			return false;
		}

		try
		{
			IEditorAPI editorApi = GameContext.IEditorAPI;
			TerrainGridInfo gridInfo = editorApi.GetTerrainGridInfo();
			_generator = CreateGenerator(editorApi, gridInfo, seedText, _generator.Parameters);
			Progress = new MapGenerationProgress(MapGenerationStage.Preparing, 0f, true, false, false, null);
			return true;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapGenerationService] Failed to rebuild procedural generation for a new seed: {exception}");
			CancelPreview();
			GameContext.MapEditorHud?.ShowFeedback(string.Format(TranslationServer.Translate("Map generation failed: {0}"), exception.Message));
			return false;
		}
	}

	public MapGenerationProgress Advance(int budgetMilliseconds)
	{
		if (_generator == null)
		{
			return Progress;
		}

		try
		{
			_generator.Advance(budgetMilliseconds);

			if (_generator.HasFailed)
			{
				string failureMessage = string.IsNullOrWhiteSpace(_generator.FailureMessage)
					? TranslationServer.Translate("Map generation failed. The map was left unchanged.")
					: _generator.FailureMessage;
				CancelPreview();
				GameContext.MapEditorHud?.ShowFeedback(failureMessage);
				Progress = new MapGenerationProgress(MapGenerationStage.Finishing, 0f, false, false, true, failureMessage);
				return Progress;
			}

			if (_generator.IsComplete)
			{
				Progress = new MapGenerationProgress(MapGenerationStage.Finishing, 100f, false, true, false, null);
				return Progress;
			}

			Progress = new MapGenerationProgress(_generator.Stage, _generator.Percent, true, false, false, null);
			return Progress;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapGenerationService] Procedural generation failed: {exception}");
			string failureMessage = string.Format(TranslationServer.Translate("Map generation failed: {0}"), exception.Message);
			CancelPreview();
			GameContext.MapEditorHud?.ShowFeedback(failureMessage);
			Progress = new MapGenerationProgress(MapGenerationStage.Finishing, 0f, false, false, true, failureMessage);
			return Progress;
		}
	}

	public void ApplyPreview()
	{
		if (_generator == null || _previewSnapshot == null)
		{
			return;
		}

		MapStateSnapshot? after = MapStateSnapshot.CreateSnapshot();
		if (after != null)
		{
			EditorHistoryManager.RecordAction(new MapSnapshotAction(_previewSnapshot, after));
		}

		_generator = null;
		_previewSnapshot = null;
		Progress = new MapGenerationProgress(MapGenerationStage.Finishing, 100f, false, true, false, null);
		GameContext.MapEditorHud?.ShowFeedback(TranslationServer.Translate("Random map applied."));
	}

	public void CancelPreview()
	{
		_generator = null;
		Progress = new MapGenerationProgress(MapGenerationStage.Preparing, 0f, false, false, false, null);

		if (_previewSnapshot == null)
		{
			return;
		}

		MapStateSnapshot.Restore(_previewSnapshot, updateMapSize: false);
		_previewSnapshot = null;
	}

	public void DiscardRun()
	{
		_generator = null;
		_previewSnapshot = null;
	}

	public TerrainImageImportResult ImportTerrainFromImage(string imagePath)
	{
		DiscardRun();

		try
		{
			var importer = new TerrainImageImporter();
			TerrainImageImportResult result = importer.Import(GameContext.IEditorAPI, imagePath);
			if (result.Success)
			{
				GameContext.MapEditorHud?.ShowFeedback(TranslationServer.Translate("Terrain imported from minimap image successfully!"));
			}
			else if (!string.IsNullOrWhiteSpace(result.Message))
			{
				GameContext.MapEditorHud?.ShowFeedback(result.Message);
			}

			return result;
		}
		catch (Exception exception)
		{
			GameLog.LogError($"[MapGenerationService] Terrain image import failed: {exception}");
			GameContext.IEditorAPI.ResetMapToBlank();
			string message = string.Format(TranslationServer.Translate("Terrain import failed: {0}"), exception.Message);
			GameContext.MapEditorHud?.ShowFeedback(message);
			return new TerrainImageImportResult { Success = false, Message = message };
		}
	}

	private ProceduralMapGenerator CreateGenerator(IEditorAPI editorApi, TerrainGridInfo gridInfo, string? seedText, MapGenerationParameters parameters)
	{
		TerrainMaterialPalette palette = editorApi.GetTerrainMaterialPalette();
		var treeIds = editorApi.ResolvePropAssetIds(TreeKeywords, true);
		var decorationIds = editorApi.ResolvePropAssetIds(DecorationKeywords, false);
		if (decorationIds.Count == 0)
		{
			decorationIds = editorApi.ResolvePropAssetIds(new[] { string.Empty }, false);
		}

		decorationIds = decorationIds.FindAll(propId => !treeIds.Exists(treeId => string.Equals(treeId, propId, StringComparison.OrdinalIgnoreCase)));

		return new ProceduralMapGenerator(
			editorApi,
			gridInfo.Width,
			gridInfo.Depth,
			gridInfo.QuadSize,
			seedText,
			parameters,
			palette,
			treeIds.ToArray(),
			decorationIds.ToArray(),
			ClearGeneratedProps);
	}

	private static void ClearGeneratedProps()
	{
		GameContext.Host?.ClearPlacedEntitiesExternal();
	}

	private bool FailRegistration(string message)
	{
		string failureMessage = string.Format(TranslationServer.Translate("Map generation failed: {0}"), message);
		GameContext.MapEditorHud?.ShowFeedback(failureMessage);
		Progress = new MapGenerationProgress(MapGenerationStage.Finishing, 0f, false, false, true, failureMessage);
		return false;
	}
}
