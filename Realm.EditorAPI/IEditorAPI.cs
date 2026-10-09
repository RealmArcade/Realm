using System;
using System.Collections.Generic;

namespace Realm.MapAPI;

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
}
