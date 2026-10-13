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
}