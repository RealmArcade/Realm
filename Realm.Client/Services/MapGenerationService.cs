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

	public bool IsRunning => _generator != null;

	public MapGenerationProgress Progress { get; private set; } = new(MapGenerationStage.Preparing, 0f, false, false, false, null);

	public string LastTerrainImageFolder { get; set; } = string.Empty;

	public bool StartProcedural(string seedText, MapGenerationParameters parameters)
	{
		_generator = null;
		Progress = new MapGenerationProgress(MapGenerationStage.Preparing, 0f, true, false, false, null);

		try
		{
			IEditorAPI editorApi = GameContext.IEditorAPI;
			editorApi.ResetMapToBlank();
			TerrainGridInfo gridInfo = editorApi.GetTerrainGridInfo();
			if (gridInfo.Width <= 0 || gridInfo.Depth <= 0)
			{
				return FailRegistration("The editor has no terrain grid to generate into.");
			}

			TerrainMaterialPalette palette = editorApi.GetTerrainMaterialPalette();
			var treeIds = editorApi.ResolvePropAssetIds(TreeKeywords, true);
			var decorationIds = editorApi.ResolvePropAssetIds(DecorationKeywords, false);
			if (decorationIds.Count == 0)
			{
				decorationIds = editorApi.ResolvePropAssetIds(new[] { string.Empty }, false);
			}

			decorationIds = decorationIds.FindAll(propId => !treeIds.Exists(treeId => string.Equals(treeId, propId, StringComparison.OrdinalIgnoreCase)));

			_generator = new ProceduralMapGenerator(
				editorApi,
				gridInfo.Width,
				gridInfo.Depth,
				gridInfo.QuadSize,
				seedText,
				parameters,
				palette,
				treeIds.ToArray(),
				decorationIds.ToArray());
			return true;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapGenerationService] Failed to start procedural generation: {exception}");
			return FailRegistration(exception.Message);
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
					? TranslationServer.Translate("Map generation failed. The map was left blank.")
					: _generator.FailureMessage;
				_generator = null;
				GameContext.IEditorAPI.ResetMapToBlank();
				GameContext.MapEditorHud?.ShowFeedback(failureMessage);
				Progress = new MapGenerationProgress(MapGenerationStage.Finishing, 0f, false, false, true, failureMessage);
				return Progress;
			}

			if (_generator.IsComplete)
			{
				_generator = null;
				GameContext.MapEditorHud?.ShowFeedback(TranslationServer.Translate("Random map generated successfully!"));
				Progress = new MapGenerationProgress(MapGenerationStage.Finishing, 100f, false, true, false, null);
				return Progress;
			}

			Progress = new MapGenerationProgress(_generator.Stage, _generator.Percent, true, false, false, null);
			return Progress;
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapGenerationService] Procedural generation failed: {exception}");
			_generator = null;
			string failureMessage = string.Format(TranslationServer.Translate("Map generation failed: {0}"), exception.Message);
			GameContext.IEditorAPI.ResetMapToBlank();
			GameContext.MapEditorHud?.ShowFeedback(failureMessage);
			Progress = new MapGenerationProgress(MapGenerationStage.Finishing, 0f, false, false, true, failureMessage);
			return Progress;
		}
	}

	public void DiscardRun()
	{
		_generator = null;
	}

	public void Cancel()
	{
		if (_generator == null)
		{
			return;
		}

		_generator = null;
		Progress = new MapGenerationProgress(MapGenerationStage.Preparing, 0f, false, false, false, null);
		GameContext.IEditorAPI.ResetMapToBlank();
		GameContext.MapEditorHud?.ShowFeedback(TranslationServer.Translate("Generation cancelled. The map was left blank."));
	}

	public TerrainImageImportResult ImportTerrainFromImage(string imagePath)
	{
		_generator = null;

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
			GD.PrintErr($"[MapGenerationService] Terrain image import failed: {exception}");
			GameContext.IEditorAPI.ResetMapToBlank();
			string message = string.Format(TranslationServer.Translate("Terrain import failed: {0}"), exception.Message);
			GameContext.MapEditorHud?.ShowFeedback(message);
			return new TerrainImageImportResult { Success = false, Message = message };
		}
	}

	private bool FailRegistration(string message)
	{
		string failureMessage = string.Format(TranslationServer.Translate("Map generation failed: {0}"), message);
		GameContext.MapEditorHud?.ShowFeedback(failureMessage);
		Progress = new MapGenerationProgress(MapGenerationStage.Finishing, 0f, false, false, true, failureMessage);
		return false;
	}
}
