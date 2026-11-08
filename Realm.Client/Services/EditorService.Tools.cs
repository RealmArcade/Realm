using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Ecs.Components.Core;
using Realm.EditorAPI;

namespace Realm.Client.Services;

public partial class EditorService
{
	private long _lastTerrainMeshRebuildMilliseconds = long.MinValue;
	private bool _terrainGeometryDirty;
	private bool _terrainHeightsDirty;
	private bool _terrainPathingDirty;

	public void ApplyActiveTerrainToolAt(float worldX, float worldY, float worldZ, float delta, bool isFirstClick)
	{
		GameHost? host = GameContext.Host;
		if (host?.GroundTerrain == null)
		{
			return;
		}

		Vector3 worldPosition = new Vector3(worldX, worldY, worldZ);
		var positions = new List<Vector3> { worldPosition };
		if (host.EditorMirrorMode != MirrorMode.None)
		{
			foreach (MirroredTransform mirroredTransform in GetMirroredTransforms(worldPosition, 0.0f, host.EditorMirrorMode))
			{
				positions.Add(mirroredTransform.Position);
			}
		}

		int pathingMask = 0;
		bool pathingAdd = true;
		if (host.ActiveEditorTool == EditorTool.PaintPathing && GameContext.MapEditorHud != null)
		{
			pathingMask = GameContext.MapEditorHud.GetSelectedPathingMask();
			pathingAdd = GameContext.MapEditorHud.IsPathingAddMode();
		}

		EditorPaintSurfaceMode paintSurfaceMode = host.ResolvePaintSurfaceMode();
		bool applyGround = paintSurfaceMode != EditorPaintSurfaceMode.CliffOnly;
		bool applyCliff = paintSurfaceMode != EditorPaintSurfaceMode.GroundOnly;
		float targetBlockHeight = host.ActiveEditorTool == EditorTool.Height ? host.EditorExactHeight : host.EditorBlockLevelHeight;

		bool anyModified = false;
		foreach (Vector3 position in positions)
		{
			TerrainEditResult result = TerrainEdit.Apply(
				position,
				delta,
				host.ActiveEditorTool,
				host.EditorBrushRadius,
				host.EditorBrushStrength,
				host.EditorBrushIsSquare,
				host.EditorBlockMode,
				targetBlockHeight,
				EditorPaintTextureIndex,
				EditorCliffPaintTextureIndex,
				pathingMask,
				pathingAdd,
				isFirstClick,
				applyGround,
				applyCliff);

			if (!result.HeightsModified && !result.SplatModified && !result.PathingModified)
			{
				continue;
			}

			Rect2I affectedRegion = new Rect2I(result.MinX - 2, result.MinZ - 2, result.MaxX - result.MinX + 5, result.MaxZ - result.MinZ + 5);
			TerrainFlushRegion = TerrainFlushRegion.HasValue ? TerrainFlushRegion.Value.Merge(affectedRegion) : affectedRegion;
			_terrainGeometryDirty = true;
			if (result.HeightsModified) _terrainHeightsDirty = true;
			if (result.PathingModified) _terrainPathingDirty = true;
			anyModified = true;
		}

		if (anyModified)
		{
			FlushTerrainModifications(isFirstClick);
		}
	}

	private void FlushTerrainModifications(bool isFirstClick)
	{
		GameHost? host = GameContext.Host;
		if (host == null)
		{
			return;
		}

		host.EditorHasUnsavedChanges = true;
		long nowMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		if (!isFirstClick && nowMilliseconds - _lastTerrainMeshRebuildMilliseconds < TerrainMeshRebuildPeriodMs)
		{
			return;
		}

		_lastTerrainMeshRebuildMilliseconds = nowMilliseconds;
		if (!TerrainFlushRegion.HasValue || host.GroundTerrain == null)
		{
			return;
		}

		Rect2I flushRegion = TerrainFlushRegion.Value;
		host.GroundTerrain.UpdateMeshAndPhysics(false, false, flushRegion, _terrainHeightsDirty);
		if (_terrainHeightsDirty)
		{
			host.AlignAllEntitiesToTerrainExternal(flushRegion);
		}

		if (_terrainPathingDirty)
		{
			host.UpdatePathingOverlay();
		}

		ClearPendingTerrainFlushState();
	}

	public void FlushTerrainMeshAndPhysics()
	{
		GameHost? host = GameContext.Host;
		if (!_terrainGeometryDirty || !TerrainFlushRegion.HasValue || host?.GroundTerrain == null)
		{
			return;
		}

		Rect2I flushRegion = TerrainFlushRegion.Value;
		host.GroundTerrain.UpdateMeshAndPhysics(_terrainHeightsDirty, false, flushRegion, _terrainHeightsDirty);
		if (_terrainHeightsDirty)
		{
			host.AlignAllEntitiesToTerrainExternal(flushRegion);
		}

		if (_terrainPathingDirty)
		{
			host.UpdatePathingOverlay();
		}

		ClearPendingTerrainFlushState();
		_lastTerrainMeshRebuildMilliseconds = long.MinValue;
	}

	private void ClearPendingTerrainFlushState()
	{
		TerrainFlushRegion = null;
		_terrainGeometryDirty = false;
		_terrainHeightsDirty = false;
		_terrainPathingDirty = false;
	}

	public void FloodFillTerrainAt(float worldX, float worldY, float worldZ, int textureIndex, bool cliff) =>
		EditorToolOperations.FloodFillTerrainAt(worldX, worldY, worldZ, textureIndex, cliff);

	public void FloodFillPathingAt(float worldX, float worldY, float worldZ, int pathingMask, bool addPathing) =>
		EditorToolOperations.FloodFillPathingAt(worldX, worldY, worldZ, pathingMask, addPathing);

	public void FloodFillWaterAt(float worldX, float worldY, float worldZ, bool removeWater) =>
		EditorToolOperations.FloodFillWaterAt(worldX, worldY, worldZ, removeWater);

	public void CopySelectedArea() => EditorToolOperations.CopySelectedArea();

	public void CutSelectedArea() => EditorToolOperations.CutSelectedArea();

	public void EraseSelectedArea() => EditorToolOperations.EraseSelectedArea();

	public void RotateSelectedObject(float degrees) => EditorToolOperations.RotateSelectedObject(degrees);

	public void ScaleSelectedObject(float multiplier) => EditorToolOperations.ScaleSelectedObject(multiplier);

	public void ResetSelectedObjectScale() => EditorToolOperations.ResetSelectedObjectScale();

	public EditorOperationResult ClearMap() => EditorToolOperations.ClearMap();

	public EditorOperationResult ResizeMap(int width, int depth) => EditorToolOperations.ResizeMap(width, depth);

	public EditorOperationResult DeleteSelectedObject() => EditorToolOperations.DeleteSelectedObject();

	public EditorOperationResult SetModelYOffset(string assetKey, float offset) => EditorToolOperations.SetModelYOffset(assetKey, offset);

	public EditorOperationResult SetModelScale(string assetKey, float scale) => EditorToolOperations.SetModelScale(assetKey, scale);

	public EditorOperationResult SetModelCollisionCircleRatio(string assetKey, float ratio) => EditorToolOperations.SetModelCollisionCircleRatio(assetKey, ratio);

	public EditorOperationResult SetModelBrightness(string assetKey, float brightness) => EditorToolOperations.SetModelBrightness(assetKey, brightness);

	public EditorOperationResult SetModelColorTint(string assetKey, float red, float green, float blue, float alpha) =>
		EditorToolOperations.SetModelColorTint(assetKey, red, green, blue, alpha);

	public EditorOperationResult SetModelIgnorePlayerColor(string assetKey, bool ignorePlayerColor) => EditorToolOperations.SetModelIgnorePlayerColor(assetKey, ignorePlayerColor);

	public EditorOperationResult SetModelDespillPlayerColor(string assetKey, bool despillPlayerColor) => EditorToolOperations.SetModelDespillPlayerColor(assetKey, despillPlayerColor);

	public EditorOperationResult SetModelNormalizeLuminance(string assetKey, bool normalizeLuminance) => EditorToolOperations.SetModelNormalizeLuminance(assetKey, normalizeLuminance);

	public EditorOperationResult SetModelSpawnShader(string assetKey, string shaderKey) => EditorToolOperations.SetModelSpawnShader(assetKey, shaderKey);

	public EditorOperationResult SetModelDeathShader(string assetKey, string shaderKey) => EditorToolOperations.SetModelDeathShader(assetKey, shaderKey);

	public EditorOperationResult SetModelEnableProceduralAnimation(string assetKey, bool enable) => EditorToolOperations.SetModelEnableProceduralAnimation(assetKey, enable);

	public EditorOperationResult SetModelProceduralAnimation(string assetKey, string animationId) => EditorToolOperations.SetModelProceduralAnimation(assetKey, animationId);

	public EditorOperationResult RefreshPlacedObjectModels(string assetKey = "") => EditorToolOperations.RefreshPlacedObjectModels(assetKey);
}
