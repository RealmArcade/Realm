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

		GameContext.Host.SetModelScale(_assetKey, snapshot.Scale);
		GameContext.Host.SetModelYOffset(_assetKey, snapshot.YOffset);
		GameContext.Host.SetModelCollisionCircleRatio(_assetKey, snapshot.CollisionCircleRatio);
		GameContext.Host.SetModelBrightness(_assetKey, snapshot.Brightness);
		GameContext.Host.SetModelColorTint(_assetKey, snapshot.ColorTint);
		GameContext.Host.SetModelNormalizeLuminance(_assetKey, snapshot.NormalizeLuminance);
		GameContext.Host.SetModelIgnorePlayerColor(_assetKey, snapshot.IgnorePlayerColor);
		GameContext.Host.SetModelDespillPlayerColor(_assetKey, snapshot.DespillPlayerColor);
		GameContext.Host.SetModelSpawnShader(_assetKey, snapshot.SpawnShader);
		GameContext.Host.SetModelDeathShader(_assetKey, snapshot.DeathShader);
		GameContext.Host.SetModelEnableProceduralAnimation(_assetKey, snapshot.EnableProceduralAnimation);
		GameContext.Host.SetModelProceduralAnimation(_assetKey, snapshot.ProceduralAnimation);

		Prop3D.InvalidateModelPathCache(_assetKey);
		ModelCache.InvalidateModelPath(_assetKey);
		if (!string.IsNullOrEmpty(snapshot.ModelPath))
		{
			Prop3D.InvalidateModelPathCache(snapshot.ModelPath);
			ModelCache.InvalidateModelPath(snapshot.ModelPath);
		}

		GameContext.Host.RefreshAllPlacedObjectModels(_assetKey);
		GameContext.Host.FlushModelYOffsetSave();
		GameContext.Host.FlushModelCollisionCircleSave();
	}
}