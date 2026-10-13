using Realm.Client.Core;

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
		if (GameHost.Instance == null || string.IsNullOrEmpty(_assetKey)) return;

		EntityVisualEditDialog.UpdateStaticRegistryModelAndVisualMode(_category, _assetKey, snapshot.ModelPath, snapshot.VisualMode);

		GameHost.Instance.SetModelScale(_assetKey, snapshot.Scale);
		GameHost.Instance.SetModelYOffset(_assetKey, snapshot.YOffset);
		GameHost.Instance.SetModelCollisionCircleRatio(_assetKey, snapshot.CollisionCircleRatio);
		GameHost.Instance.SetModelBrightness(_assetKey, snapshot.Brightness);
		GameHost.Instance.SetModelColorTint(_assetKey, snapshot.ColorTint);
		GameHost.Instance.SetModelNormalizeLuminance(_assetKey, snapshot.NormalizeLuminance);
		GameHost.Instance.SetModelIgnorePlayerColor(_assetKey, snapshot.IgnorePlayerColor);
		GameHost.Instance.SetModelDespillPlayerColor(_assetKey, snapshot.DespillPlayerColor);
		GameHost.Instance.SetModelSpawnShader(_assetKey, snapshot.SpawnShader);
		GameHost.Instance.SetModelDeathShader(_assetKey, snapshot.DeathShader);
		GameHost.Instance.SetModelEnableProceduralAnimation(_assetKey, snapshot.EnableProceduralAnimation);
		GameHost.Instance.SetModelProceduralAnimation(_assetKey, snapshot.ProceduralAnimation);

		Prop3D.InvalidateModelPathCache(_assetKey);
		ModelCache.InvalidateModelPath(_assetKey);
		if (!string.IsNullOrEmpty(snapshot.ModelPath))
		{
			Prop3D.InvalidateModelPathCache(snapshot.ModelPath);
			ModelCache.InvalidateModelPath(snapshot.ModelPath);
		}

		GameHost.Instance.RefreshAllPlacedObjectModels(_assetKey);
		GameHost.Instance.FlushModelYOffsetSave();
		GameHost.Instance.FlushModelCollisionCircleSave();
	}
}