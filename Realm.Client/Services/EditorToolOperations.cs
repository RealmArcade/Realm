using System;
using Godot;
using Realm.Client.Core;
using Realm.EditorAPI;

namespace Realm.Client.Services;

/// <summary>
///     The tool partial's operations that need no editor state: each one reaches the live host through
///     <see cref="GameContext" /> and nothing else, so it can run without a paint session and holds no state. EditorService
///     keeps every one as a forwarder, because that is the surface IEditorAPI is implemented against; the terrain paint
///     flush and its dirty flags stay on EditorService because they are the editor's own pending flush state.
/// </summary>
public static class EditorToolOperations
{
	public static void FloodFillTerrainAt(float worldX, float worldY, float worldZ, int textureIndex, bool cliff)
	{
		GameContext.Host?.PerformFloodFill(new Vector3(worldX, worldY, worldZ), textureIndex, cliff);
	}

	public static void FloodFillPathingAt(float worldX, float worldY, float worldZ, int pathingMask, bool addPathing)
	{
		GameContext.Host?.PerformFloodFillPathing(new Vector3(worldX, worldY, worldZ), pathingMask, addPathing);
	}

	public static void FloodFillWaterAt(float worldX, float worldY, float worldZ, bool removeWater)
	{
		GameContext.Host?.PerformWaterFloodFill(new Vector3(worldX, worldY, worldZ), removeWater);
	}

	public static void CopySelectedArea()
	{
		GameContext.Host?.PerformCopyAreaExternal();
	}

	public static void CutSelectedArea()
	{
		GameContext.Host?.PerformCutAreaExternal();
	}

	public static void EraseSelectedArea()
	{
		GameContext.Host?.PerformEraseAreaExternal();
	}

	public static void RotateSelectedObject(float degrees)
	{
		GameContext.Host?.RotateSelectedObject(degrees);
	}

	public static void ScaleSelectedObject(float multiplier)
	{
		GameContext.Host?.ScaleSelectedObject(multiplier);
	}

	public static void ResetSelectedObjectScale()
	{
		GameContext.Host?.ResetScaleSelectedObject();
	}

	public static EditorOperationResult ClearMap()
	{
		if (GameContext.Host == null) return EditorOperationResult.Failed("there is no map open in the editor");

		GameContext.Host.ClearMapEntirely();
		return EditorOperationResult.Succeeded();
	}

	public static EditorOperationResult ResizeMap(int width, int depth)
	{
		if (GameContext.Host?.GroundTerrain == null) return EditorOperationResult.Failed("there is no map open in the editor");

		GameContext.Host.ResizeMapExternal(width, depth);
		return EditorOperationResult.Succeeded();
	}

	public static EditorOperationResult DeleteSelectedObject()
	{
		if (GameContext.Host == null) return EditorOperationResult.Failed("there is no map open in the editor");

		(bool success, string error) = GameContext.Host.DeleteSelectedObject();
		return success ? EditorOperationResult.Succeeded() : EditorOperationResult.Failed(error);
	}

	public static EditorOperationResult SetModelYOffset(string assetKey, float offset) => WithHost(host => host.SetModelYOffset(assetKey, offset));

	public static EditorOperationResult SetModelScale(string assetKey, float scale) => WithHost(host => host.SetModelScale(assetKey, scale));

	public static EditorOperationResult SetModelCollisionCircleRatio(string assetKey, float ratio) => WithHost(host => host.SetModelCollisionCircleRatio(assetKey, ratio));

	public static EditorOperationResult SetModelBrightness(string assetKey, float brightness) => WithHost(host => host.SetModelBrightness(assetKey, brightness));

	public static EditorOperationResult SetModelColorTint(string assetKey, float red, float green, float blue, float alpha) =>
		WithHost(host => host.SetModelColorTint(assetKey, new Color(red, green, blue, alpha)));

	public static EditorOperationResult SetModelIgnorePlayerColor(string assetKey, bool ignorePlayerColor) => WithHost(host => host.SetModelIgnorePlayerColor(assetKey, ignorePlayerColor));

	public static EditorOperationResult SetModelDespillPlayerColor(string assetKey, bool despillPlayerColor) => WithHost(host => host.SetModelDespillPlayerColor(assetKey, despillPlayerColor));

	public static EditorOperationResult SetModelNormalizeLuminance(string assetKey, bool normalizeLuminance) => WithHost(host => host.SetModelNormalizeLuminance(assetKey, normalizeLuminance));

	public static EditorOperationResult SetModelSpawnShader(string assetKey, string shaderKey) => WithHost(host => host.SetModelSpawnShader(assetKey, shaderKey));

	public static EditorOperationResult SetModelDeathShader(string assetKey, string shaderKey) => WithHost(host => host.SetModelDeathShader(assetKey, shaderKey));

	public static EditorOperationResult SetModelEnableProceduralAnimation(string assetKey, bool enable) => WithHost(host => host.SetModelEnableProceduralAnimation(assetKey, enable));

	public static EditorOperationResult SetModelProceduralAnimation(string assetKey, string animationId) => WithHost(host => host.SetModelProceduralAnimation(assetKey, animationId));

	public static EditorOperationResult RefreshPlacedObjectModels(string assetKey = "") => WithHost(host => host.RefreshAllPlacedObjectModels(string.IsNullOrEmpty(assetKey) ? null : assetKey));

	private static EditorOperationResult WithHost(Action<GameHost> operation)
	{
		if (GameContext.Host == null) return EditorOperationResult.Failed("there is no map open in the editor");

		operation(GameContext.Host);
		return EditorOperationResult.Succeeded();
	}
}
