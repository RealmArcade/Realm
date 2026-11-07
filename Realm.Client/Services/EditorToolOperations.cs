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
internal static class EditorToolOperations
{
	internal static void FloodFillTerrainAt(float worldX, float worldY, float worldZ, int textureIndex, bool cliff)
	{
		GameContext.Host?.PerformFloodFill(new Vector3(worldX, worldY, worldZ), textureIndex, cliff);
	}

	internal static void FloodFillPathingAt(float worldX, float worldY, float worldZ, int pathingMask, bool addPathing)
	{
		GameContext.Host?.PerformFloodFillPathing(new Vector3(worldX, worldY, worldZ), pathingMask, addPathing);
	}

	internal static void FloodFillWaterAt(float worldX, float worldY, float worldZ, bool removeWater)
	{
		GameContext.Host?.PerformWaterFloodFill(new Vector3(worldX, worldY, worldZ), removeWater);
	}

	internal static void CopySelectedArea()
	{
		GameContext.Host?.PerformCopyAreaExternal();
	}

	internal static void CutSelectedArea()
	{
		GameContext.Host?.PerformCutAreaExternal();
	}

	internal static void EraseSelectedArea()
	{
		GameContext.Host?.PerformEraseAreaExternal();
	}

	internal static void RotateSelectedObject(float degrees)
	{
		GameContext.Host?.RotateSelectedObject(degrees);
	}

	internal static void ScaleSelectedObject(float multiplier)
	{
		GameContext.Host?.ScaleSelectedObject(multiplier);
	}

	internal static void ResetSelectedObjectScale()
	{
		GameContext.Host?.ResetScaleSelectedObject();
	}

	internal static EditorOperationResult ClearMap()
	{
		if (GameContext.Host == null) return EditorOperationResult.Failed("there is no map open in the editor");

		GameContext.Host.ClearMapEntirely();
		return EditorOperationResult.Succeeded();
	}

	internal static EditorOperationResult ResizeMap(int width, int depth)
	{
		if (GameContext.Host?.GroundTerrain == null) return EditorOperationResult.Failed("there is no map open in the editor");

		GameContext.Host.ResizeMapExternal(width, depth);
		return EditorOperationResult.Succeeded();
	}

	internal static EditorOperationResult DeleteSelectedObject()
	{
		if (GameContext.Host == null) return EditorOperationResult.Failed("there is no map open in the editor");

		(bool success, string error) = GameContext.Host.DeleteSelectedObject();
		return success ? EditorOperationResult.Succeeded() : EditorOperationResult.Failed(error);
	}

	internal static EditorOperationResult SetModelYOffset(string assetKey, float offset) => WithHost(host => host.SetModelYOffset(assetKey, offset));

	internal static EditorOperationResult SetModelScale(string assetKey, float scale) => WithHost(host => host.SetModelScale(assetKey, scale));

	internal static EditorOperationResult SetModelCollisionCircleRatio(string assetKey, float ratio) => WithHost(host => host.SetModelCollisionCircleRatio(assetKey, ratio));

	internal static EditorOperationResult SetModelBrightness(string assetKey, float brightness) => WithHost(host => host.SetModelBrightness(assetKey, brightness));

	internal static EditorOperationResult SetModelColorTint(string assetKey, float red, float green, float blue, float alpha) =>
		WithHost(host => host.SetModelColorTint(assetKey, new Color(red, green, blue, alpha)));

	internal static EditorOperationResult SetModelIgnorePlayerColor(string assetKey, bool ignorePlayerColor) => WithHost(host => host.SetModelIgnorePlayerColor(assetKey, ignorePlayerColor));

	internal static EditorOperationResult SetModelDespillPlayerColor(string assetKey, bool despillPlayerColor) => WithHost(host => host.SetModelDespillPlayerColor(assetKey, despillPlayerColor));

	internal static EditorOperationResult SetModelNormalizeLuminance(string assetKey, bool normalizeLuminance) => WithHost(host => host.SetModelNormalizeLuminance(assetKey, normalizeLuminance));

	internal static EditorOperationResult SetModelSpawnShader(string assetKey, string shaderKey) => WithHost(host => host.SetModelSpawnShader(assetKey, shaderKey));

	internal static EditorOperationResult SetModelDeathShader(string assetKey, string shaderKey) => WithHost(host => host.SetModelDeathShader(assetKey, shaderKey));

	internal static EditorOperationResult SetModelEnableProceduralAnimation(string assetKey, bool enable) => WithHost(host => host.SetModelEnableProceduralAnimation(assetKey, enable));

	internal static EditorOperationResult SetModelProceduralAnimation(string assetKey, string animationId) => WithHost(host => host.SetModelProceduralAnimation(assetKey, animationId));

	internal static EditorOperationResult RefreshPlacedObjectModels(string assetKey = "") => WithHost(host => host.RefreshAllPlacedObjectModels(string.IsNullOrEmpty(assetKey) ? null : assetKey));

	private static EditorOperationResult WithHost(Action<GameHost> operation)
	{
		if (GameContext.Host == null) return EditorOperationResult.Failed("there is no map open in the editor");

		operation(GameContext.Host);
		return EditorOperationResult.Succeeded();
	}
}
