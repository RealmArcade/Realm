namespace Realm.Client.UI.MapEditor;

public class EntityVisualEditUndoAction : IEditorAction
{
	private readonly string _assetKey;
	private readonly string _category;
	private readonly EntityVisualEditDialog.VisualOverridesSnapshot _before;
	private readonly EntityVisualEditDialog.VisualOverridesSnapshot _after;

	public EntityVisualEditUndoAction(string assetKey, string category, EntityVisualEditDialog.VisualOverridesSnapshot before, EntityVisualEditDialog.VisualOverridesSnapshot after)
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

	private void ApplySnapshot(EntityVisualEditDialog.VisualOverridesSnapshot snapshot)
	{
		if (Realm.Client.Core.GameHost.Instance == null || string.IsNullOrEmpty(_assetKey)) return;

		EntityVisualEditDialog.UpdateStaticRegistryModelAndVisualMode(_category, _assetKey, snapshot.ModelPath, snapshot.VisualMode);

		Realm.Client.Core.GameHost.Instance.SetModelScale(_assetKey, snapshot.Scale);
		Realm.Client.Core.GameHost.Instance.SetModelYOffset(_assetKey, snapshot.YOffset);
		Realm.Client.Core.GameHost.Instance.SetModelCollisionCircleRatio(_assetKey, snapshot.CollisionCircleRatio);
		Realm.Client.Core.GameHost.Instance.SetModelBrightness(_assetKey, snapshot.Brightness);
		Realm.Client.Core.GameHost.Instance.SetModelColorTint(_assetKey, snapshot.ColorTint);
		Realm.Client.Core.GameHost.Instance.SetModelNormalizeLuminance(_assetKey, snapshot.NormalizeLuminance);
		Realm.Client.Core.GameHost.Instance.SetModelIgnorePlayerColor(_assetKey, snapshot.IgnorePlayerColor);
		Realm.Client.Core.GameHost.Instance.SetModelDespillPlayerColor(_assetKey, snapshot.DespillPlayerColor);
		Realm.Client.Core.GameHost.Instance.SetModelSpawnShader(_assetKey, snapshot.SpawnShader);
		Realm.Client.Core.GameHost.Instance.SetModelDeathShader(_assetKey, snapshot.DeathShader);
		Realm.Client.Core.GameHost.Instance.SetModelEnableProceduralAnimation(_assetKey, snapshot.EnableProceduralAnimation);
		Realm.Client.Core.GameHost.Instance.SetModelProceduralAnimation(_assetKey, snapshot.ProceduralAnimation);

		Realm.Client.Prop3D.InvalidateModelPathCache(_assetKey);
		ModelCache.InvalidateModelPath(_assetKey);
		if (!string.IsNullOrEmpty(snapshot.ModelPath))
		{
			Realm.Client.Prop3D.InvalidateModelPathCache(snapshot.ModelPath);
			ModelCache.InvalidateModelPath(snapshot.ModelPath);
		}

		Realm.Client.Core.GameHost.Instance.RefreshAllPlacedObjectModels(_assetKey);
		Realm.Client.Core.GameHost.Instance.FlushModelYOffsetSave();
		Realm.Client.Core.GameHost.Instance.FlushModelCollisionCircleSave();
	}
}