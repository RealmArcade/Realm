using System;
using System.Collections.Generic;
using System.Numerics;

namespace Realm.MapAPI;

/// <summary>
/// Represents a player in the game session, providing a unified facade over both
/// server-authoritative player simulation state and client-targeted RPC actions.
/// </summary>
public interface IPlayer
{
	/// <summary>
	/// Gets the zero-based slot index of this player.
	/// </summary>
	int Index { get; }

	/// <summary>
	/// Gets the zero-based slot index of this player (alias for <see cref="Index"/>).
	/// </summary>
	int Id => Index;

	/// <summary>
	/// Gets the display name of this player.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Gets the display name of this player.
	/// </summary>
	string GetName();

	/// <summary>
	/// Gets a value indicating whether this player slot is occupied by an active human or AI participant.
	/// </summary>
	bool IsActive { get; }

	/// <summary>
	/// Sets whether this player slot is active in the game session.
	/// </summary>
	void SetActive(bool active);

	/// <summary>
	/// Gets or sets a value indicating whether this player slot is controlled by a computer AI.
	/// </summary>
	bool IsComputer { get; set; }

	/// <summary>
	/// Gets or sets the team index that this player belongs to.
	/// </summary>
	int Team { get; set; }

	/// <summary>
	/// Gets the team index that this player belongs to.
	/// </summary>
	int GetTeam();

	/// <summary>
	/// Assigns this player to the specified numbered team.
	/// </summary>
	void SetTeam(int teamIndex);

	/// <summary>
	/// Configures whether this player and another player slot treat each other as passive allies.
	/// </summary>
	void SetAllied(int otherPlayerIndex, bool allied);

	/// <summary>
	/// Configures whether this player and another player treat each other as passive allies.
	/// </summary>
	void SetAllied(IPlayer otherPlayer, bool allied);

	/// <summary>
	/// Returns true if this player and another player slot treat each other as allies.
	/// </summary>
	bool IsAlliedWith(int otherPlayerIndex);

	/// <summary>
	/// Returns true if this player and another player treat each other as allies.
	/// </summary>
	bool IsAlliedWith(IPlayer otherPlayer);

	/// <summary>
	/// Kicks this player from the game session.
	/// </summary>
	void Kick(string reason = "Kicked by host");

	/// <summary>
	/// Gets the start location for this player in 3D world space.
	/// </summary>
	Vector3 StartLocation { get; }

	/// <summary>
	/// Gets the start location for this player in 3D world space.
	/// </summary>
	Vector3 GetStartLocation();

	/// <summary>
	/// Gets the preferred ISO language code of this player (e.g. "en", "es").
	/// </summary>
	string Language { get; }

	/// <summary>
	/// Gets the standard hex color code string (RRGGBB) corresponding to this player's slot.
	/// </summary>
	string ColorHex { get; }

	/// <summary>
	/// Gets the player's name formatted with BBCode color markup matching their slot color.
	/// </summary>
	string ColoredName { get; }

	// ── Resources & Population (Authoritative State) ────────────────────────────

	/// <summary>
	/// Gets or sets the player's gold resource amount.
	/// </summary>
	float Gold { get; set; }

	/// <summary>
	/// Gets the player's gold resource amount.
	/// </summary>
	float GetGold();

	/// <summary>
	/// Sets the player's gold resource amount.
	/// </summary>
	void SetGold(float amount);

	/// <summary>
	/// Adds the specified delta to the player's gold resource amount.
	/// </summary>
	void AdjustGold(float delta);

	/// <summary>
	/// Gets or sets the player's wood (lumber) resource amount.
	/// </summary>
	float Wood { get; set; }

	/// <summary>
	/// Gets the player's wood (lumber) resource amount.
	/// </summary>
	float GetWood();

	/// <summary>
	/// Sets the player's wood (lumber) resource amount.
	/// </summary>
	void SetWood(float amount);

	/// <summary>
	/// Adds the specified delta to the player's wood (lumber) resource amount.
	/// </summary>
	void AdjustWood(float delta);

	/// <summary>
	/// Gets or sets the player's stone resource amount.
	/// </summary>
	float Stone { get; set; }

	/// <summary>
	/// Gets the player's stone resource amount.
	/// </summary>
	float GetStone();

	/// <summary>
	/// Sets the player's stone resource amount.
	/// </summary>
	void SetStone(float amount);

	/// <summary>
	/// Adds the specified delta to the player's stone resource amount.
	/// </summary>
	void AdjustStone(float delta);

	/// <summary>
	/// Gets the current population count for this player.
	/// </summary>
	int CurrentPopulation { get; }

	/// <summary>
	/// Gets the current population count for this player.
	/// </summary>
	int GetCurrentPopulation();

	/// <summary>
	/// Gets or sets the maximum population capacity for this player.
	/// </summary>
	int MaxPopulation { get; set; }

	/// <summary>
	/// Gets the maximum population capacity for this player.
	/// </summary>
	int GetMaxPopulation();

	/// <summary>
	/// Sets the maximum population capacity for this player.
	/// </summary>
	void SetMaxPopulation(int max);

	/// <summary>
	/// Gets or sets the enemy kill count for this player.
	/// </summary>
	int Kills { get; set; }

	/// <summary>
	/// Gets the enemy kill count for this player.
	/// </summary>
	int GetKills();

	/// <summary>
	/// Sets the enemy kill count for this player.
	/// </summary>
	void SetKills(int kills);

	// ── Units & Tech (Authoritative Simulation) ─────────────────────────────────

	/// <summary>
	/// Spawns a unit owned by this player at the specified 2D ground coordinates (X, Z).
	/// </summary>
	IUnit SpawnUnit(string unitTypeId, Vector2 position, bool executeSpawnShader = true, bool bypassPopulation = false);

	/// <summary>
	/// Spawns a unit owned by this player at the specified 3D world position (using X, Z ground coordinates).
	/// </summary>
	IUnit SpawnUnit(string unitTypeId, Vector3 position, bool executeSpawnShader = true, bool bypassPopulation = false);

	/// <summary>
	/// Spawns a unit owned by this player at the center of the specified coordinate region.
	/// </summary>
	IUnit SpawnUnit(string unitTypeId, Coordinate coordinate, bool executeSpawnShader = true, bool bypassPopulation = false);

	/// <summary>
	/// Retrieves all alive units owned by this player.
	/// </summary>
	IEnumerable<IUnit> GetUnits();

	/// <summary>
	/// Retrieves all alive units owned by this player that satisfy the given filter predicate.
	/// </summary>
	IEnumerable<IUnit> GetUnits(Func<IUnit, bool> filter);

	/// <summary>
	/// Returns the number of alive units owned by this player matching the optional filter.
	/// </summary>
	int CountUnits(Func<IUnit, bool>? filter = null);

	/// <summary>
	/// Orders all units owned by this player to attack-move to the destination.
	/// </summary>
	void IssueAttackMoveOrder(Vector3 destination);

	/// <summary>
	/// Attempts to collect a ground loot item using an eligible unit owned by this player.
	/// </summary>
	/// <param name="lootItem">The ground loot item to collect.</param>
	/// <returns>True if an eligible unit successfully collected the item; otherwise, false.</returns>
	bool CollectLootItem(ILootItem lootItem);

	/// <summary>
	/// Attempts to collect a ground loot item by its ID using an eligible unit owned by this player.
	/// </summary>
	/// <param name="lootItemId">The unique identifier of the ground loot item to collect.</param>
	/// <returns>True if an eligible unit successfully collected the item; otherwise, false.</returns>
	bool CollectLootItem(int lootItemId);

	/// <summary>
	/// Gets the research level of a technology upgrade for this player.
	/// </summary>
	int GetTechLevel(string techId);

	/// <summary>
	/// Sets the research level of a technology upgrade for this player.
	/// </summary>
	void SetTechLevel(string techId, int level);

	/// <summary>
	/// Adds to the research level of a technology upgrade for this player.
	/// </summary>
	void AddTechLevel(string techId, int delta = 1);

	// ── Client RPC Actions (Targeted to this Player's Client) ───────────────────

	/// <summary>
	/// Smoothly pans this player's camera to the specified 3D world position.
	/// </summary>
	void PanCameraTo(Vector3 position, float duration = 0f);

	/// <summary>
	/// Smoothly moves/pans this player's camera to the specified 3D world position.
	/// </summary>
	void MoveCameraTo(Vector3 position, float duration = 0f);

	/// <summary>
	/// Shakes this player's camera to simulate an explosion or impact.
	/// </summary>
	void ShakeCamera(float intensity, float duration);

	/// <summary>
	/// Restricts this player's camera movement limits in world space coordinates.
	/// Clamps the camera position immediately if currently out of bounds.
	/// </summary>
	/// <param name="minX">The minimum X world boundary.</param>
	/// <param name="minZ">The minimum Z world boundary.</param>
	/// <param name="maxX">The maximum X world boundary.</param>
	/// <param name="maxZ">The maximum Z world boundary.</param>
	void SetCameraBounds(float minX, float minZ, float maxX, float maxZ);

	/// <summary>
	/// Restricts this player's camera movement limits to the specified named coordinate marker.
	/// </summary>
	/// <param name="coordinateName">The name of the authored coordinate marker defining the camera bounds.</param>
	void SetCameraBounds(string coordinateName);

	/// <summary>
	/// Restricts this player's camera movement limits to the specified coordinate bounding region.
	/// </summary>
	/// <param name="coordinate">The coordinate bounding region defining the camera bounds.</param>
	void SetCameraBounds(Coordinate coordinate);

	/// <summary>
	/// Restricts this player's camera movement limits in world space coordinates (alias for <see cref="SetCameraBounds(float, float, float, float)"/>).
	/// </summary>
	/// <param name="minX">The minimum X world boundary.</param>
	/// <param name="minZ">The minimum Z world boundary.</param>
	/// <param name="maxX">The maximum X world boundary.</param>
	/// <param name="maxZ">The maximum Z world boundary.</param>
	void SetMapCameraBoundaries(float minX, float minZ, float maxX, float maxZ) => SetCameraBounds(minX, minZ, maxX, maxZ);

	/// <summary>
	/// Resets this player's camera movement boundaries back to the map default boundaries defined in metadata.
	/// </summary>
	void ResetCameraBounds();

	/// <summary>
	/// Resets this player's camera movement boundaries back to the map default boundaries (alias for <see cref="ResetCameraBounds"/>).
	/// </summary>
	void ResetCameraBoundaries() => ResetCameraBounds();

	/// <summary>
	/// Sets whether a specific built-in HUD element is visible for this player.
	/// </summary>
	/// <param name="element">The HUD element to show or hide.</param>
	/// <param name="visible">True to make the element visible; false to hide it.</param>
	void SetHudElementVisible(HudElement element, bool visible);

	/// <summary>
	/// Toggles whether a specific built-in HUD element is visible for this player (alias for <see cref="SetHudElementVisible(HudElement, bool)"/>).
	/// </summary>
	/// <param name="element">The HUD element to show or hide.</param>
	/// <param name="visible">True to make the element visible; false to hide it.</param>
	void ToggleUserInterfaceElement(HudElement element, bool visible) => SetHudElementVisible(element, visible);

	/// <summary>
	/// Selects the specified unit on this player's client, clearing their previous selection.
	/// </summary>
	void SelectUnit(IUnit unit);

	/// <summary>
	/// Clears the unit selection on this player's client.
	/// </summary>
	void ClearSelection();

	/// <summary>
	/// Retrieves the units currently selected by this player.
	/// </summary>
	IEnumerable<IUnit> GetSelectedUnits();

	/// <summary>
	/// Sends a text message to this player's screen.
	/// </summary>
	void SendMessage(string message);

	/// <summary>
	/// Displays floating feedback text on this player's screen with the specified RGB color.
	/// </summary>
	void ShowFeedbackText(string text, Vector3 color);

	/// <summary>
	/// Spawns a minimap ping on this player's client at the specified 3D world position.
	/// </summary>
	void PingMinimap(Vector3 position);

	/// <summary>
	/// Plays the standard warning/alert sound on this player's client.
	/// </summary>
	void PlayWarningSound();

	/// <summary>
	/// Plays the standard button/command click sound on this player's client.
	/// </summary>
	void PlayClickSound();

	/// <summary>
	/// Enters building placement mode on this player's client for the specified structure type.
	/// </summary>
	void StartBuildingPlacement(string unitTypeId);

	/// <summary>
	/// Triggers the victory sequence for this player.
	/// </summary>
	void TriggerVictory();

	/// <summary>
	/// Triggers the defeat sequence for this player.
	/// </summary>
	void TriggerDefeat(string reason = "");
}

/// <summary>
/// Default pass-through facade implementation of <see cref="IPlayer"/> that delegates
/// player-scoped operations to <see cref="IGameAPI"/>.
/// </summary>
public class PlayerFacade : IPlayer
{
	private readonly IGameAPI _api;

	public int Index { get; }

	public PlayerFacade(IGameAPI api, int playerIndex)
	{
		_api = api ?? throw new ArgumentNullException(nameof(api));
		Index = playerIndex;
	}

	public string Name => _api.GetPlayerName(Index);
	public string GetName() => _api.GetPlayerName(Index);

	public bool IsActive => _api.IsPlayerActive(Index);
	public void SetActive(bool active) => _api.SetPlayerActive(Index, active);

	public bool IsComputer
	{
		get => _api.IsPlayerComputer(Index);
		set => _api.SetPlayerComputerControlled(Index, value);
	}

	public int Team
	{
		get => _api.GetPlayerTeam(Index);
		set => _api.SetPlayerTeam(Index, value);
	}

	public int GetTeam() => _api.GetPlayerTeam(Index);
	public void SetTeam(int teamIndex) => _api.SetPlayerTeam(Index, teamIndex);

	public void SetAllied(int otherPlayerIndex, bool allied) => _api.SetPlayersAllied(Index, otherPlayerIndex, allied);
	public void SetAllied(IPlayer otherPlayer, bool allied)
	{
		if (otherPlayer != null)
			_api.SetPlayersAllied(Index, otherPlayer.Index, allied);
	}

	public bool IsAlliedWith(int otherPlayerIndex) => _api.ArePlayersAllied(Index, otherPlayerIndex);
	public bool IsAlliedWith(IPlayer otherPlayer) => otherPlayer != null && _api.ArePlayersAllied(Index, otherPlayer.Index);

	public void Kick(string reason = "Kicked by host") => _api.KickPlayer(Index, reason);

	public Vector3 StartLocation => _api.GetPlayerStartLocation(Index);
	public Vector3 GetStartLocation() => _api.GetPlayerStartLocation(Index);

	public string Language => _api.GetPlayerLanguage(Index);
	public string ColorHex => _api.GetPlayerColorHex(Index);
	public string ColoredName => _api.FormatColoredPlayerName(Index);

	public float Gold
	{
		get => _api.GetPlayerGold(Index);
		set => _api.SetPlayerGold(Index, value);
	}

	public float GetGold() => _api.GetPlayerGold(Index);
	public void SetGold(float amount) => _api.SetPlayerGold(Index, amount);
	public void AdjustGold(float delta) => _api.AdjustPlayerGold(Index, delta);

	public float Wood
	{
		get => _api.GetPlayerWood(Index);
		set => _api.SetPlayerWood(Index, value);
	}

	public float GetWood() => _api.GetPlayerWood(Index);
	public void SetWood(float amount) => _api.SetPlayerWood(Index, amount);
	public void AdjustWood(float delta) => _api.AdjustPlayerWood(Index, delta);

	public float Stone
	{
		get => _api.GetPlayerStone(Index);
		set => _api.SetPlayerStone(Index, value);
	}

	public float GetStone() => _api.GetPlayerStone(Index);
	public void SetStone(float amount) => _api.SetPlayerStone(Index, amount);
	public void AdjustStone(float delta) => _api.AdjustPlayerStone(Index, delta);

	public int CurrentPopulation => _api.GetPlayerCurrentPopulation(Index);
	public int GetCurrentPopulation() => _api.GetPlayerCurrentPopulation(Index);

	public int MaxPopulation
	{
		get => _api.GetPlayerMaxPopulation(Index);
		set => _api.SetPlayerMaxPopulation(Index, value);
	}

	public int GetMaxPopulation() => _api.GetPlayerMaxPopulation(Index);
	public void SetMaxPopulation(int max) => _api.SetPlayerMaxPopulation(Index, max);

	public int Kills
	{
		get => _api.GetPlayerKills(Index);
		set => _api.SetPlayerKills(Index, value);
	}

	public int GetKills() => _api.GetPlayerKills(Index);
	public void SetKills(int kills) => _api.SetPlayerKills(Index, kills);

	public IUnit SpawnUnit(string unitTypeId, Vector2 position, bool executeSpawnShader = true, bool bypassPopulation = false) =>
		_api.SpawnUnitForPlayer(unitTypeId, position, Index, executeSpawnShader, bypassPopulation);

	public IUnit SpawnUnit(string unitTypeId, Vector3 position, bool executeSpawnShader = true, bool bypassPopulation = false) =>
		_api.SpawnUnitForPlayer(unitTypeId, new Vector2(position.X, position.Z), Index, executeSpawnShader, bypassPopulation);

	public IUnit SpawnUnit(string unitTypeId, Coordinate coordinate, bool executeSpawnShader = true, bool bypassPopulation = false) =>
		_api.SpawnUnitForPlayer(unitTypeId, coordinate, Index, executeSpawnShader, bypassPopulation);

	public IEnumerable<IUnit> GetUnits() => _api.GetUnitsOwnedByPlayer(Index);
	public IEnumerable<IUnit> GetUnits(Func<IUnit, bool> filter) => _api.GetUnitsOwnedByPlayer(Index, filter);
	public int CountUnits(Func<IUnit, bool>? filter = null) => _api.CountUnitsOwnedByPlayer(Index, filter);
	public void IssueAttackMoveOrder(Vector3 destination) => _api.IssueAttackMoveOrderToPlayer(Index, destination);
	public bool CollectLootItem(ILootItem lootItem) => _api.CollectLootItemByPlayer(lootItem.UniqueId, Index);
	public bool CollectLootItem(int lootItemId) => _api.CollectLootItemByPlayer(lootItemId, Index);

	public int GetTechLevel(string techId) => _api.GetPlayerTechLevel(Index, techId);
	public void SetTechLevel(string techId, int level) => _api.SetPlayerTechLevel(Index, techId, level);
	public void AddTechLevel(string techId, int delta = 1) => _api.AddPlayerTechLevel(Index, techId, delta);

	public void PanCameraTo(Vector3 position, float duration = 0f) => _api.PanPlayerCameraTo(Index, position, duration);
	public void MoveCameraTo(Vector3 position, float duration = 0f) => _api.PanPlayerCameraTo(Index, position, duration);
	public void ShakeCamera(float intensity, float duration) => _api.ShakePlayerCamera(Index, intensity, duration);
	public void SetCameraBounds(float minX, float minZ, float maxX, float maxZ) => _api.SetPlayerCameraBounds(Index, minX, minZ, maxX, maxZ);
	public void SetCameraBounds(string coordinateName) => _api.SetPlayerCameraBounds(Index, coordinateName);
	public void SetCameraBounds(Coordinate coordinate) => _api.SetPlayerCameraBounds(Index, coordinate);
	public void ResetCameraBounds() => _api.ResetPlayerCameraBounds(Index);
	public void SetHudElementVisible(HudElement element, bool visible) => _api.SetPlayerHudElementVisible(Index, element, visible);
	public void SelectUnit(IUnit unit) => _api.SelectPlayerUnit(Index, unit);
	public void ClearSelection() => _api.ClearPlayerSelection(Index);
	public IEnumerable<IUnit> GetSelectedUnits() => _api.GetPlayerSelectedUnits(Index);
	public void SendMessage(string message) => _api.SendMessageToPlayer(Index, message);
	public void ShowFeedbackText(string text, Vector3 color) => _api.ShowPlayerFeedbackText(Index, text, color);
	public void PingMinimap(Vector3 position) => _api.PingPlayerMinimap(Index, position);
	public void PlayWarningSound() => _api.PlayPlayerWarningSound(Index);
	public void PlayClickSound() => _api.PlayPlayerClickSound(Index);
	public void StartBuildingPlacement(string unitTypeId) => _api.StartPlayerBuildingPlacement(Index, unitTypeId);
	public void TriggerVictory() => _api.TriggerPlayerVictory(Index);
	public void TriggerDefeat(string reason = "") => _api.TriggerPlayerDefeat(Index, reason);
}
