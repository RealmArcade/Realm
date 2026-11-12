// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Spawns a resource node at the given position.
	/// </summary>
	/// <param name="resourceType">The type of resource node (e.g., "goldmine", "tree", "rock").</param>
	/// <param name="position">The coordinates in 3D world space.</param>
	/// <param name="amount">The initial amount of resources inside the node.</param>
	void SpawnResourceNode(string resourceType, Vector3 position, float amount);

	/// <summary>
	/// Retrieves all units currently alive in the game world.
	/// </summary>
	/// <returns>A read-only span of all active unit identifiers.</returns>
	ReadOnlySpan<UnitId> GetAllUnits();

	/// <summary>
	/// Retrieves all active units within the specified radius of a 3D position.
	/// </summary>
	/// <param name="center">The center point in 3D world space.</param>
	/// <param name="radius">The radius distance check.</param>
	/// <returns>A read-only span of unit identifiers within the radius.</returns>
	ReadOnlySpan<UnitId> GetUnitsInRadius(Vector3 center, float radius);

	/// <summary>
	/// Gets the number of resource nodes.
	/// </summary>
	int ResourceNodeCount { get; }

	/// <summary>
	/// Retrieves a resource node by its index.
	/// </summary>
	/// <param name="index">The zero-based index of the resource node.</param>
	/// <returns>The resource node identifier.</returns>
	ResourceNodeId GetResourceNode(int index);

	/// <summary>
	/// Retrieves all active resource nodes in the game world.
	/// </summary>
	/// <returns>A read-only span of all active resource node identifiers.</returns>
	ReadOnlySpan<ResourceNodeId> GetResourceNodes();

	/// <summary>
	/// Displays floating feedback text on the player's screen.
	/// </summary>
	/// <param name="text">The text message to display.</param>
	/// <param name="color">The color of the text in RGB format.</param>
	void ShowFeedbackText(string text, Vector3 color);

	/// <summary>
	/// Triggers the game victory sequence.
	/// </summary>
	void TriggerVictory();

	/// <summary>
	/// Triggers the game defeat sequence.
	/// </summary>
	void TriggerDefeat();

	/// <summary>
	/// Spawns a temporary visual target indicator ring at the specified position with the given color.
	/// </summary>
	/// <param name="position">The coordinates in 3D world space.</param>
	/// <param name="color">The color of the indicator ring in RGB format.</param>
	void SpawnTargetIndicator(Vector3 position, Vector3 color);

	/// <summary>
	/// Retrieves a unit by its unique runtime identifier.
	/// </summary>
	/// <param name="uniqueId">The unique ID of the unit to retrieve.</param>
	/// <returns>The unit identifier.</returns>
	UnitId GetUnitById(int uniqueId);

	/// <summary>
	/// Generates a new map directory containing boilerplate files for a custom map.
	/// </summary>
	/// <param name="mapName">The name of the new map script class and directory.</param>
	/// <param name="targetDirectory">The parent directory path where the map directory should be created. If left null or empty, it defaults to the standard user maps folder.</param>
	void GenerateMapDirectory(string mapName, string? targetDirectory = null);

	/// <summary>
	/// Kills the specified unit naturally, triggering death animations and bounties.
	/// </summary>
	/// <param name="unit">The unit to kill.</param>
	/// <param name="executeDespawnShader">True to execute the despawn or death shader effect if configured, false to skip.</param>
	/// <param name="playDeathAnimation">True to play the death animation if available, false to skip.</param>
	void KillUnit(UnitId unit, bool executeDespawnShader = true, bool playDeathAnimation = true);

	/// <summary>
	/// Instantly removes the specified unit from the game.
	/// </summary>
	/// <param name="unit">The unit to destroy.</param>
	/// <param name="executeDespawnShader">True to execute the despawn or death shader effect if configured, false to skip.</param>
	/// <param name="playDeathAnimation">True to play the death animation if available, false to skip.</param>
	void DestroyUnit(UnitId unit, bool executeDespawnShader = true, bool playDeathAnimation = true);

	/// <summary>
	/// Retrieves the resource type identifier provided by the specified resource node (e.g. "gold", "wood", "stone").
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <returns>The resource type string.</returns>
	string GetResourceNodeType(ResourceNodeId node);

	/// <summary>
	/// Retrieves the 3D world space position of the specified resource node.
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <returns>The 3D coordinates of the resource node.</returns>
	Vector3 GetResourceNodePosition(ResourceNodeId node);

	/// <summary>
	/// Retrieves the remaining harvestable resource amount in the specified resource node.
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <returns>The remaining resource amount.</returns>
	float GetResourceNodeAmount(ResourceNodeId node);

	/// <summary>
	/// Sets the remaining harvestable resource amount in the specified resource node.
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <param name="value">The new resource amount.</param>
	void SetResourceNodeAmount(ResourceNodeId node, float value);

	/// <summary>
	/// Returns true if the specified resource node is fully depleted of resources.
	/// </summary>
	/// <param name="node">The resource node identifier.</param>
	/// <returns>True if depleted; otherwise, false.</returns>
	bool IsResourceNodeDepleted(ResourceNodeId node);

	/// <summary>
	/// Gets how many player starting points the map instance the match is playing declares.
	/// A spawn anchor joins a player slot to a named coordinate of the board, and it is authored with the board's terrain
	/// instead of in a script, so one ruleset can be played on any board that seats its players. A map that declares none
	/// answers zero, which is how a script tells a board meant for it apart from one it was pointed at by mistake.
	/// </summary>
	/// <returns>The number of spawn anchors the loaded map declares.</returns>
	int GetSpawnAnchorCount();

	/// <summary>
	/// Gets where the map instance places the starting point of one player slot.
	/// The anchor is the coordinate the board seats that slot inside, and the point is picked within it when the match
	/// begins, so two players assigned the same ground do not stand on the identical step. The point is snapped to the
	/// ground of the loaded terrain, so it is a position a starting building can be placed at and a camera can be moved to
	/// without the script reading a height field itself. A slot the map declares no usable anchor for is seated anywhere on
	/// the board, so this never answers a point a match cannot use.
	/// </summary>
	/// <param name="playerSlot">The one-based player slot the lobby seats a player in, as the map's own slot numbering states it.</param>
	/// <returns>The ground position of that slot's anchor, or a point anywhere on the board when the map declares none for it.</returns>
	Vector3 GetSpawnAnchor(int playerSlot);

	/// <summary>
	/// Reports how far a building site has been built, as a fraction from zero to one. A unit that is not a building
	/// site answers one, so a caller may ask any unit without first knowing what state it is in.
	/// </summary>
	/// <param name="unit">The building site.</param>
	/// <returns>The completed fraction of its construction time.</returns>
	float GetUnitConstructionProgress(UnitId unit);

	/// <summary>
	/// Advances a building site's construction by the given amount of work, which is how an ability that finishes part
	/// of a construction reads without the map having to know how construction time is stored.
	/// </summary>
	/// <param name="unit">The building site.</param>
	/// <param name="seconds">The amount of construction work to add, in seconds of build time.</param>
	void AdvanceUnitConstruction(UnitId unit, float seconds);

	/// <summary>
	/// Starts a building site for a player: spawns the building and schedules its construction, charging no resource and
	/// consuming no building supply. The engine advances a site nobody is working on only when the map says so, through
	/// <see cref="AdvanceUnitConstruction" />, and announces it through <see cref="OnConstructionFinished" /> like any
	/// other site, so a map that builds from an order it authored keeps the completion path it already handles.
	/// Use this instead of <see cref="SpawnUnitForPlayer(string, System.Numerics.Vector2, int, bool, bool)" /> when the building is meant to appear unfinished; that method
	/// creates a completed building.
	/// </summary>
	/// <param name="buildingTypeId">The template identifier of the building to start.</param>
	/// <param name="position">The world position of the site.</param>
	/// <param name="playerIndex">The zero-based player slot index that owns the site.</param>
	/// <param name="totalConstructionSeconds">How much build time the site needs before it completes.</param>
	/// <returns>The site unit, or an invalid handle when the template is not a building this map defines.</returns>
	UnitId SpawnBuildingUnderConstruction(string buildingTypeId, Vector3 position, int playerIndex, float totalConstructionSeconds);

	/// <summary>
	/// Turns a unit that already stands into a building site, which is how a map raises something from where it was
	/// placed instead of spawning a second entity beside it. The unit takes the work it was given before it completes and
	/// is announced through <see cref="OnConstructionFinished" /> like any other site.
	/// <para>
	/// Whether a site may be attacked, ordered or worked on is the map's own decision, so nothing but the construction
	/// itself is written here: a unit that should not be attacked is made invulnerable by the map, and a unit a worker
	/// should be able to raise is one the map made a building.
	/// </para>
	/// </summary>
	/// <param name="unit">The unit to raise as a building site.</param>
	/// <param name="totalConstructionSeconds">How much work the site needs before it completes, which has to be greater than zero.</param>
	void StartUnitConstruction(UnitId unit, float totalConstructionSeconds);

	/// <summary>
	/// Holds a construction site short of completion: it takes work up to the given fraction of its construction time and
	/// stops there, so it is drawn as a site that is nearly finished and never becomes a building until the ceiling is
	/// lifted. This is what lets a map stand a site on the board before it is known what is being raised there, because a
	/// site that could complete would become a building nobody chose.
	/// </summary>
	/// <param name="unit">The building site to hold, or to release when the fraction given is one.</param>
	/// <param name="finishedFraction">
	/// The furthest the site may be built, from zero to one. One releases the site and is the state every site starts in;
	/// a site held below one keeps the progress it has already been given, shortened to the new ceiling.
	/// </param>
	void SetUnitConstructionCeiling(UnitId unit, float finishedFraction);

	/// <summary>
	/// Reveals what a construction site is being raised as: the site takes the chosen building's template, so every screen
	/// draws that building from the next description it receives, and the work it now needs is the building's own. The
	/// work the site has already taken stays with it rather than being paid for twice, which is what makes a site a player
	/// has laboured at before choosing worth as much as the choice itself.
	/// <para>
	/// A site held by <see cref="SetUnitConstructionCeiling" /> stays held until the map lifts the ceiling, so revealing a
	/// site does not on its own make it possible for the site to complete.
	/// </para>
	/// </summary>
	/// <param name="site">The building site to reveal.</param>
	/// <param name="buildingTypeId">The template of the building the site is being raised as.</param>
	/// <param name="totalConstructionSeconds">How much work the revealed site needs in total, which has to be greater than zero.</param>
	/// <returns>True when the site now stands as that building, or false when it or the template is unknown to this map.</returns>
	bool RevealBuildingSite(UnitId site, string buildingTypeId, float totalConstructionSeconds);
}
