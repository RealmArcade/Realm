using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

public class EntityVisualEditUndoAction : IEditorAction
{
	private readonly string _assetKey;
	private readonly string _category;
	private readonly VisualOverridesSnapshot _before;
	private readonly VisualOverridesSnapshot _after;

	public EntityVisualEditUndoAction(string assetKey, string category, VisualOverridesSnapshot before, VisualOverridesSnapshot after)
	{
		_assetKey = assetKey;
		_category = category;
		_before = before;
		_after = after;
	}

	public void Undo()
	{
		ApplySnapshot(_before);
	}

	public void Redo()
	{
		ApplySnapshot(_after);
	}

	private void ApplySnapshot(VisualOverridesSnapshot snapshot)
	{
		if (GameContext.Host == null || string.IsNullOrEmpty(_assetKey)) return;

		EntityVisualEditDialog.UpdateStaticRegistryModelAndVisualMode(_category, _assetKey, snapshot.ModelPath, snapshot.VisualMode);

		GameContext.IEditorAPI.SetModelScale(_assetKey, snapshot.Scale);
		GameContext.IEditorAPI.SetModelYOffset(_assetKey, snapshot.YOffset);
		GameContext.IEditorAPI.SetModelCollisionCircleRatio(_assetKey, snapshot.CollisionCircleRatio);
		GameContext.IEditorAPI.SetModelBrightness(_assetKey, snapshot.Brightness);
		GameContext.IEditorAPI.SetModelColorTint(_assetKey, snapshot.ColorTint.R, snapshot.ColorTint.G, snapshot.ColorTint.B, snapshot.ColorTint.A);
		GameContext.IEditorAPI.SetModelNormalizeLuminance(_assetKey, snapshot.NormalizeLuminance);
		GameContext.IEditorAPI.SetModelIgnorePlayerColor(_assetKey, snapshot.IgnorePlayerColor);
		GameContext.IEditorAPI.SetModelDespillPlayerColor(_assetKey, snapshot.DespillPlayerColor);
		GameContext.IEditorAPI.SetModelSpawnShader(_assetKey, snapshot.SpawnShader);
		GameContext.IEditorAPI.SetModelDeathShader(_assetKey, snapshot.DeathShader);
		GameContext.IEditorAPI.SetModelEnableProceduralAnimation(_assetKey, snapshot.EnableProceduralAnimation);
		GameContext.IEditorAPI.SetModelProceduralAnimation(_assetKey, snapshot.ProceduralAnimation);

		Prop3D.InvalidateModelPathCache(_assetKey);
		ModelCache.InvalidateModelPath(_assetKey);
		if (!string.IsNullOrEmpty(snapshot.ModelPath))
		{
			Prop3D.InvalidateModelPathCache(snapshot.ModelPath);
			ModelCache.InvalidateModelPath(snapshot.ModelPath);
		}

		GameContext.IEditorAPI.RefreshPlacedObjectModels(_assetKey);
		GameContext.Host.FlushModelYOffsetSave();
		GameContext.Host.FlushModelCollisionCircleSave();
	}
}