using System;
using System.Collections.Generic;

namespace Realm.EditorAPI;

/// <summary>
/// Provides domain service capabilities for map editing operations.
/// </summary>
public interface IEditorAPI
{
	/// <summary>
	/// Creates or updates a named map coordinate trigger region with grid extents.
	/// </summary>
	/// <param name="name">Unique name identifier of the coordinate.</param>
	/// <param name="minCellX">West grid cell coordinate boundary.</param>
	/// <param name="minCellZ">South grid cell coordinate boundary.</param>
	/// <param name="maxCellX">East grid cell coordinate boundary.</param>
	/// <param name="maxCellZ">North grid cell coordinate boundary.</param>
	/// <returns>True if coordinate was successfully created or updated; otherwise false.</returns>
	bool CommitCoordinate(string name, int minCellX, int minCellZ, int maxCellX, int maxCellZ);

	/// <summary>
	/// Deletes a named map coordinate trigger region by name.
	/// </summary>
	/// <param name="name">Unique name identifier of the coordinate to delete.</param>
	/// <returns>True if the coordinate existed and was deleted; otherwise false.</returns>
	bool DeleteCoordinate(string name);

	/// <summary>
	/// Selects a named map coordinate trigger region and highlights its outline on the terrain.
	/// </summary>
	/// <param name="name">Unique name identifier of the coordinate to select, or empty string to clear selection.</param>
	void SelectCoordinate(string name);

	/// <summary>
	/// Searches indexed assets across the specified asset types matching terms in the query, ranking by matching term count.
	/// </summary>
	/// <param name="assetTypes">Array of asset categories to filter on, or empty to search all categories.</param>
	/// <param name="query">Space or delimiter separated search string.</param>
	/// <param name="limit">Maximum number of assets to return.</param>
	/// <returns>List of matching assets ordered descending by the number of matching terms.</returns>
	List<Asset> SearchAssets(AssetType[] assetTypes, string query, int limit = 5);

	/// <summary>
	/// Extracts distinct indexed keywords associated with a template in metadata.json to form a reusable search query.
	/// </summary>
	/// <param name="templateSlug">Slug or template ID of the object in metadata.</param>
	/// <returns>A space-separated string of validated search terms indexed in asset storage.</returns>
	string ExtractSearchQuery(string templateSlug);

	/// <summary>
	/// Imports the specified assets into the map workspace if they exist in content addressable storage but are not yet registered in manifest.json.
	/// </summary>
	/// <param name="assets">List of assets to import.</param>
	void ImportAssets(List<Asset> assets);

	/// <summary>
	/// Replaces asset fields on a metadata template with randomly chosen matching assets found via extracted keywords.
	/// </summary>
	/// <param name="templateSlug">Slug or template ID of the object in metadata.</param>
	/// <param name="overwriteExisting">True to overwrite existing non-empty asset fields; false to only populate empty ones.</param>
	void AssignRandomAssetsToTemplate(string templateSlug, bool overwriteExisting = false);

	/// <summary>
	/// Assigns random assets to all templates matching the given template types.
	/// </summary>
	/// <param name="templateTypes">Array of template categories to process.</param>
	/// <param name="overwriteExisting">True to overwrite existing non-empty asset fields; false to only populate empty ones.</param>
	void AssignRandomAssetsToTemplates(TemplateType[] templateTypes, bool overwriteExisting = false);

	/// <summary>
	/// Assigns random assets to all templates across all template categories.
	/// </summary>
	/// <param name="overwriteExisting">True to overwrite existing non-empty asset fields; false to only populate empty ones.</param>
	void AssignRandomAssetsToTemplates(bool overwriteExisting = false);

	/// <summary>
	/// Reads the authoritative terrain grid dimensions and spacing.
	/// </summary>
	/// <returns>The current terrain grid description.</returns>
	TerrainGridInfo GetTerrainGridInfo();

	/// <summary>
	/// Resolves the terrain material swatch indices used when writing generated splat maps.
	/// The returned indices always point at valid swatch slots for the attached map.
	/// </summary>
	/// <returns>The resolved water, ground, rock and cliff material indices.</returns>
	TerrainMaterialPalette GetTerrainMaterialPalette();

	/// <summary>
	/// Reads the cliff texture swatch currently selected in the editor paint controls.
	/// </summary>
	/// <returns>A valid cliff splat map swatch index.</returns>
	int GetSelectedCliffTextureIndex();

	/// <summary>
	/// Discards the entire current map (entities, terrain, selection, history and camera bounds)
	/// and rebuilds a clean blank grid, resetting the workspace file-watch timestamps afterwards.
	/// </summary>
	void ResetMapToBlank();

	/// <summary>
	/// Replaces every terrain cell, splat map entry and pathing code with generated heights and rebuilds
	/// the terrain mesh, water, collision and pathing overlay in one authoritative commit. Water is not
	/// written here: negative tiers become dry basins, so call <see cref="FillWaterPoolAtCell"/> afterwards
	/// to flood each basin through the same cliff-bounded search the editor water tool uses. Adjacent cell
	/// corners are welded to a single height per grid node so the rebuilt surface stays connected.
	/// </summary>
	/// <param name="width">Grid width in cells; must match <see cref="GetTerrainGridInfo"/>.</param>
	/// <param name="depth">Grid depth in cells; must match <see cref="GetTerrainGridInfo"/>.</param>
	/// <param name="tiers">Row-major tier per cell (X fastest). Negative tiers are basins below ground level, zero is flat ground and positive tiers are raised land.</param>
	/// <param name="groundMaterials">Row-major ground material swatch index per cell, matching the tier array layout.</param>
	/// <param name="cliffMaterialIndex">Swatch index written as a solid weight across the cliff splat map.</param>
	/// <returns>True when the layout matched the live grid and was committed; otherwise false.</returns>
	bool ApplyGeneratedTerrain(int width, int depth, ReadOnlySpan<sbyte> tiers, ReadOnlySpan<int> groundMaterials, int cliffMaterialIndex);

	/// <summary>
	/// Rebuilds the terrain mesh and collision from the authoritative terrain state.
	/// </summary>
	void RebuildTerrain();

	/// <summary>
	/// Refreshes editor visuals that depend on terrain geometry after a destructive rebuild.
	/// </summary>
	/// <param name="regenerateMinimap">True to regenerate the editor minimap from the new terrain.</param>
	void RefreshEditorAfterTerrainChange(bool regenerateMinimap);

	/// <summary>
	/// Marks the current map document as modified so the editor prompts to save it.
	/// </summary>
	void MarkDocumentModified();

	/// <summary>
	/// Resets the workspace file-watch timestamps so a destructive internal rebuild is not later
	/// reported as an external modification.
	/// </summary>
	void ResetWorkspaceFileWatchTimestamps();

	/// <summary>
	/// Spawns a prop or resource node in the editor at a terrain-snapped world position.
	/// </summary>
	/// <param name="propId">Registry id of the prop or resource template.</param>
	/// <param name="x">World X position.</param>
	/// <param name="y">Requested world Y position; the service snaps it to the terrain surface.</param>
	/// <param name="z">World Z position.</param>
	/// <param name="rotationY">Yaw in degrees.</param>
	/// <param name="scale">Uniform scale multiplier.</param>
	/// <returns>True when the prop was spawned; otherwise false.</returns>
	bool PlaceProp(string propId, float x, float y, float z, float rotationY, float scale);

	/// <summary>
	/// Resolves prop and resource template ids whose id, display name or model path matches any keyword.
	/// </summary>
	/// <param name="keywords">Case-insensitive keywords to match, for example "tree" or "palm".</param>
	/// <param name="includeResources">True to also search the resource registry (trees and ore nodes live there).</param>
	/// <returns>Matching template ids, or an empty list when nothing matches.</returns>
	List<string> ResolvePropAssetIds(string[] keywords, bool includeResources);

	/// <summary>
	/// Applies the active terrain-brush tool at a world position, including mirror copies and the deferred
	/// mesh, collision and pathing flush.
	/// </summary>
	/// <param name="worldX">World X position of the brush centre.</param>
	/// <param name="worldY">World Y position of the brush centre.</param>
	/// <param name="worldZ">World Z position of the brush centre.</param>
	/// <param name="delta">Frame delta driving continuous brush strength.</param>
	/// <param name="isFirstClick">True on the initial press so the first stroke flushes immediately.</param>
	void ApplyActiveTerrainToolAt(float worldX, float worldY, float worldZ, float delta, bool isFirstClick);

	/// <summary>
	/// Flood-fills ground or cliff textures from a world position using the active mirror mode.
	/// </summary>
	/// <param name="worldX">World X position of the fill origin.</param>
	/// <param name="worldY">World Y position of the fill origin.</param>
	/// <param name="worldZ">World Z position of the fill origin.</param>
	/// <param name="textureIndex">Ground texture swatch index to paint.</param>
	/// <param name="cliff">True to fill the cliff splat map instead of the ground splat map.</param>
	void FloodFillTerrainAt(float worldX, float worldY, float worldZ, int textureIndex, bool cliff);

	/// <summary>
	/// Flood-fills pathing flags from a world position using the active mirror mode.
	/// </summary>
	/// <param name="worldX">World X position of the fill origin.</param>
	/// <param name="worldY">World Y position of the fill origin.</param>
	/// <param name="worldZ">World Z position of the fill origin.</param>
	/// <param name="pathingMask">Bitmask of pathing flags to add or remove.</param>
	/// <param name="addPathing">True to add flags; false to remove them.</param>
	void FloodFillPathingAt(float worldX, float worldY, float worldZ, int pathingMask, bool addPathing);

	/// <summary>
	/// Flood-fills a water pool from a single terrain cell using the same cliff-bounded search as the
	/// editor water tool, and writes a flat water surface at the requested world height. Each filled cell
	/// stores the surface offset for its own tier, so the pool reads as one level body of water and the
	/// water pathing codes and procedural decals match a pool painted by hand.
	/// </summary>
	/// <param name="cellX">Grid X of the seed cell, normally the deepest cell of a basin.</param>
	/// <param name="cellZ">Grid Z of the seed cell.</param>
	/// <param name="waterSurfaceY">World Y of the water surface; the fill stops at terrain that rises above it.</param>
	/// <returns>True when at least one cell was filled; false when the seed is outside the grid, already wet or cannot spread.</returns>
	bool FillWaterPoolAtCell(int cellX, int cellZ, float waterSurfaceY);

	/// <summary>
	/// Flood-fills or clears water from a world position using the active water profile and mirror mode.
	/// </summary>
	/// <param name="worldX">World X position of the fill origin.</param>
	/// <param name="worldY">World Y position of the fill origin.</param>
	/// <param name="worldZ">World Z position of the fill origin.</param>
	/// <param name="removeWater">True to remove water; false to add it.</param>
	void FloodFillWaterAt(float worldX, float worldY, float worldZ, bool removeWater);

	/// <summary>
	/// Copies the current rectangular selection into the editor clipboard.
	/// </summary>
	void CopySelectedArea();

	/// <summary>
	/// Copies the current rectangular selection and erases it from the map.
	/// </summary>
	void CutSelectedArea();

	/// <summary>
	/// Erases terrain, pathing and entities inside the current rectangular selection.
	/// </summary>
	void EraseSelectedArea();

	/// <summary>
	/// Rotates the selected editor object around its Y axis.
	/// </summary>
	/// <param name="degrees">Rotation delta in degrees.</param>
	void RotateSelectedObject(float degrees);

	/// <summary>
	/// Scales the selected editor object by a multiplier.
	/// </summary>
	/// <param name="multiplier">Scale delta to apply.</param>
	void ScaleSelectedObject(float multiplier);

	/// <summary>
	/// Restores the selected editor object to its unscaled size.
	/// </summary>
	void ResetSelectedObjectScale();

	/// <summary>
	/// Deletes the currently selected editor object.
	/// </summary>
	/// <returns>Whether something was deleted, and why not when nothing was.</returns>
	EditorOperationResult DeleteSelectedObject();

	/// <summary>
	/// Discards everything currently in the map and leaves a blank grid of the same size.
	/// </summary>
	/// <returns>Whether the map was cleared, and why not when it was not.</returns>
	EditorOperationResult ClearMap();

	/// <summary>
	/// Resizes the map grid, keeping the terrain and entities that still fit.
	/// The requested size is rounded to the editor's grid multiple and clamped to its supported range,
	/// so the committed size may differ from the requested one; read it back with <see cref="GetTerrainGridInfo"/>.
	/// </summary>
	/// <param name="width">Requested width in cells.</param>
	/// <param name="depth">Requested depth in cells.</param>
	/// <returns>Whether the map was resized, and why not when it was not.</returns>
	EditorOperationResult ResizeMap(int width, int depth);

	/// <summary>Sets how far a model is lifted above the terrain when it is placed.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="offset">Vertical offset in world units.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelYOffset(string assetKey, float offset);

	/// <summary>Sets the uniform size a model is drawn at when it is placed.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="scale">Uniform scale multiplier.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelScale(string assetKey, float scale);

	/// <summary>Sets the collision radius of a model as a fraction of its drawn radius.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="ratio">Collision radius divided by drawn radius.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelCollisionCircleRatio(string assetKey, float ratio);

	/// <summary>Sets the brightness multiplier a model is drawn with.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="brightness">Brightness multiplier.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelBrightness(string assetKey, float brightness);

	/// <summary>Sets the colour tint a model is multiplied by when it is drawn.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="red">Red channel, 0 to 1.</param>
	/// <param name="green">Green channel, 0 to 1.</param>
	/// <param name="blue">Blue channel, 0 to 1.</param>
	/// <param name="alpha">Alpha channel, 0 to 1.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelColorTint(string assetKey, float red, float green, float blue, float alpha);

	/// <summary>Whether a model keeps its own colours instead of being tinted by the owner's player colour.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="ignorePlayerColor">True to keep the model's own colours.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelIgnorePlayerColor(string assetKey, bool ignorePlayerColor);

	/// <summary>Whether a model removes the player colour already painted into its textures.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="despillPlayerColor">True to remove the painted player colour.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelDespillPlayerColor(string assetKey, bool despillPlayerColor);

	/// <summary>Whether a model's colour is normalised so lighting differences between assets do not show.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="normalizeLuminance">True to normalise luminance.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelNormalizeLuminance(string assetKey, bool normalizeLuminance);

	/// <summary>Sets the effect played when a model is created.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="shaderKey">Registered shader key, or empty for none.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelSpawnShader(string assetKey, string shaderKey);

	/// <summary>Sets the effect played when a model is destroyed.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="shaderKey">Registered shader key, or empty for none.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelDeathShader(string assetKey, string shaderKey);

	/// <summary>Whether a model plays a procedural animation instead of a baked one.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="enable">True to enable the procedural animation.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelEnableProceduralAnimation(string assetKey, bool enable);

	/// <summary>Selects which procedural animation a model plays.</summary>
	/// <param name="assetKey">Map asset key the model belongs to.</param>
	/// <param name="animationId">Registered procedural animation id.</param>
	/// <returns>Whether the change was recorded.</returns>
	EditorOperationResult SetModelProceduralAnimation(string assetKey, string animationId);

	/// <summary>Redraws every placed object of an asset so a change to how it looks is visible at once.</summary>
	/// <param name="assetKey">Map asset key to redraw, or empty to redraw every placed object.</param>
	/// <returns>Whether anything was redrawn.</returns>
	EditorOperationResult RefreshPlacedObjectModels(string assetKey = "");
}
