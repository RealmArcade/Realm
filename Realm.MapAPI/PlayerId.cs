using System.Numerics;
using Realm.Ecs;

namespace Realm.MapAPI;

/// <summary>
/// Adds the map script surface of a player slot: its units, its camera and the notifications it receives.
/// </summary>
/// <remarks>
/// The handle itself is a plain index owned by the shared simulation, so the simulation can name this subject
/// without the script surface. What a script reads answers straight out of the world the process is simulating,
/// and everything that changes the world routes through the authoritative host imports.
/// </remarks>
public static unsafe class PlayerIdScriptExtensions
{
	extension(PlayerId playerId)
	{
	/// <summary>
	/// Gets the display name of this player.
	/// </summary>
	public string Name => GameAPI.Current.GetPlayerName(playerId.Value);

	/// <summary>
	/// Gets a value indicating whether this player slot is occupied by an active human or AI participant.
	/// </summary>
	public bool IsActive => playerId.Value >= 0 && playerId.Value < EcsSharedMemory.MAX_PLAYERS && EcsSharedMemory.GetComponent<ScriptPlayer>(ComponentIds.ScriptPlayer, playerId.Value)->Active;

	/// <summary>
	/// Sets whether this player slot is active in the game session.
	/// </summary>
	/// <param name="active">True if the slot is active; otherwise false.</param>
	public void SetActive(bool active) => GameAPI.Current.SetPlayerActive(playerId.Value, active);

	/// <summary>
	/// Modifies the gold resource amount for this player by a signed delta.
	/// </summary>
	/// <param name="delta">The amount of gold to add (or subtract if negative).</param>
	public void AdjustGold(float delta) => GameAPI.Current.AdjustPlayerGold(playerId.Value, delta);

	/// <summary>
	/// Modifies the wood resource amount for this player by a signed delta.
	/// </summary>
	/// <param name="delta">The amount of wood to add (or subtract if negative).</param>
	public void AdjustWood(float delta) => GameAPI.Current.AdjustPlayerWood(playerId.Value, delta);

	/// <summary>
	/// Modifies the stone resource amount for this player by a signed delta.
	/// </summary>
	/// <param name="delta">The amount of stone to add (or subtract if negative).</param>
	public void AdjustStone(float delta) => GameAPI.Current.AdjustPlayerStone(playerId.Value, delta);

	/// <summary>
	/// Gets the current population count consumed by this player's living units.
	/// </summary>
	public int CurrentPopulation => playerId.Value >= 0 && playerId.Value < EcsSharedMemory.MAX_PLAYERS ? EcsSharedMemory.GetComponent<PlayerPopulation>(ComponentIds.PlayerPopulation, playerId.Value + 1)->Current : 0;

	/// <summary>
	/// Spawns a new unit of the specified type for this player at the given 2D ground coordinates.
	/// </summary>
	/// <param name="unitTypeId">The unit archetype identifier.</param>
	/// <param name="position">The 2D world coordinates.</param>
	/// <param name="executeSpawnShader">Whether to play the material spawn shader animation.</param>
	/// <param name="bypassPopulation">Whether to bypass the player's maximum population check.</param>
	/// <returns>The spawned <see cref="UnitId"/>.</returns>
	public UnitId SpawnUnit(string unitTypeId, Vector2 position, bool executeSpawnShader = true, bool bypassPopulation = false) =>
		GameAPI.Current.SpawnUnitForPlayer(unitTypeId, position, playerId.Value, executeSpawnShader, bypassPopulation);

	/// <summary>
	/// Spawns a new unit of the specified type for this player at the given 3D world coordinates.
	/// </summary>
	/// <param name="unitTypeId">The unit archetype identifier.</param>
	/// <param name="position">The 3D world coordinates.</param>
	/// <param name="executeSpawnShader">Whether to play the material spawn shader animation.</param>
	/// <param name="bypassPopulation">Whether to bypass the player's maximum population check.</param>
	/// <returns>The spawned <see cref="UnitId"/>.</returns>
	public UnitId SpawnUnit(string unitTypeId, Vector3 position, bool executeSpawnShader = true, bool bypassPopulation = false) =>
		GameAPI.Current.SpawnUnitForPlayer(unitTypeId, new Vector2(position.X, position.Z), playerId.Value, executeSpawnShader, bypassPopulation);

	/// <summary>
	/// Retrieves a read-only span of all unit identifiers currently owned by this player.
	/// </summary>
	/// <returns>A read-only span of unit identifiers.</returns>
	public ReadOnlySpan<UnitId> GetUnits() => GameAPI.Current.GetUnitsOwnedByPlayer(playerId.Value);

	/// <summary>
	/// Sends a direct chat message notification to this player.
	/// </summary>
	/// <param name="message">The text message to transmit.</param>
	public void SendMessage(string message) => GameAPI.Current.SendMessageToPlayer(playerId.Value, message);

	/// <summary>
	/// Triggers the match victory sequence for this player.
	/// </summary>
	public void TriggerVictory() => GameAPI.Current.TriggerPlayerVictory(playerId.Value);

	/// <summary>
	/// Triggers the match defeat sequence for this player.
	/// </summary>
	/// <param name="reason">An optional defeat explanation message.</param>
	public void TriggerDefeat(string reason = "") => GameAPI.Current.TriggerPlayerDefeat(playerId.Value, reason);

	/// <summary>
	/// Configures whether this player and another player slot treat each other as passive allies.
	/// </summary>
	/// <param name="otherPlayer">The other player identifier.</param>
	/// <param name="allied">True if allied; otherwise false.</param>
	public void SetAllied(PlayerId otherPlayer, bool allied) => GameAPI.Current.SetPlayersAllied(playerId.Value, otherPlayer.Value, allied);

	/// <summary>
	/// Returns true if this player and another player slot treat each other as allies.
	/// </summary>
	/// <param name="otherPlayer">The other player identifier.</param>
	/// <returns>True if allied; otherwise false.</returns>
	public bool IsAlliedWith(PlayerId otherPlayer) => GameAPI.Current.ArePlayersAllied(playerId.Value, otherPlayer.Value);

	/// <summary>
	/// Kicks this player from the game session.
	/// </summary>
	/// <param name="reason">An optional kick explanation message.</param>
	public void Kick(string reason = "Kicked by host") => GameAPI.Current.KickPlayer(playerId.Value, reason);

	/// <summary>
	/// Gets the start location for this player in 3D world space.
	/// </summary>
	public Vector3 StartLocation => GameAPI.Current.GetPlayerStartLocation(playerId.Value);

	/// <summary>
	/// Gets the research level of a technology upgrade for this player.
	/// </summary>
	/// <param name="techId">The technology identifier.</param>
	/// <returns>The research level.</returns>
	public int GetTechLevel(string techId) => GameAPI.Current.GetPlayerTechLevel(playerId.Value, techId);

	/// <summary>
	/// Sets the research level of a technology upgrade for this player.
	/// </summary>
	/// <param name="techId">The technology identifier.</param>
	/// <param name="level">The research level to assign.</param>
	public void SetTechLevel(string techId, int level) => GameAPI.Current.SetPlayerTechLevel(playerId.Value, techId, level);

	/// <summary>
	/// Increments the research level of a technology upgrade for this player by delta.
	/// </summary>
	/// <param name="techId">The technology identifier.</param>
	/// <param name="delta">The amount to add to the research level.</param>
	public void AddTechLevel(string techId, int delta = 1) => GameAPI.Current.AddPlayerTechLevel(playerId.Value, techId, delta);

	/// <summary>
	/// Smoothly pans this player's camera to the specified 3D world position.
	/// </summary>
	/// <param name="position">The destination coordinates.</param>
	/// <param name="duration">Duration of the camera pan transition in seconds.</param>
	public void PanCameraTo(Vector3 position, float duration = 0f) => GameAPI.Current.PanPlayerCameraTo(playerId.Value, position, duration);

	/// <summary>
	/// Smoothly moves or pans this player's camera to the specified 3D world position (alias for <see cref="PanCameraTo(Vector3, float)"/>).
	/// </summary>
	/// <param name="position">The destination coordinates.</param>
	/// <param name="duration">Duration of the camera pan transition in seconds.</param>
	public void MoveCameraTo(Vector3 position, float duration = 0f) => playerId.PanCameraTo(position, duration);

	/// <summary>
	/// Shakes this player's camera to simulate an explosion or impact.
	/// </summary>
	/// <param name="intensity">The vibration intensity amplitude.</param>
	/// <param name="duration">The shake duration in seconds.</param>
	public void ShakeCamera(float intensity, float duration) => GameAPI.Current.ShakePlayerCamera(playerId.Value, intensity, duration);

	/// <summary>
	/// Restricts this player's camera movement limits in world space coordinates.
	/// </summary>
	/// <param name="minX">The minimum X world boundary.</param>
	/// <param name="minZ">The minimum Z world boundary.</param>
	/// <param name="maxX">The maximum X world boundary.</param>
	/// <param name="maxZ">The maximum Z world boundary.</param>
	public void SetCameraBounds(float minX, float minZ, float maxX, float maxZ) =>
		GameAPI.Current.SetPlayerCameraBounds(playerId.Value, minX, minZ, maxX, maxZ);

	/// <summary>
	/// Restricts this player's camera movement limits to the specified authored coordinate region.
	/// </summary>
	/// <param name="coordinate">The coordinate marker region.</param>
	public void SetCameraBounds(Coordinate coordinate) => GameAPI.Current.SetPlayerCameraBounds(playerId.Value, coordinate);

	/// <summary>
	/// Restricts this player's camera movement limits to the named authored coordinate region.
	/// </summary>
	/// <param name="coordinateName">The name of the coordinate region.</param>
	public void SetCameraBounds(string coordinateName) => GameAPI.Current.SetPlayerCameraBounds(playerId.Value, coordinateName);

	/// <summary>
	/// Resets this player's camera movement boundaries back to the map default boundaries.
	/// </summary>
	public void ResetCameraBounds() => GameAPI.Current.ResetPlayerCameraBounds(playerId.Value);

	/// <summary>
	/// Sets whether a specific built-in HUD element is visible for this player.
	/// </summary>
	/// <param name="element">The HUD element to show or hide.</param>
	/// <param name="visible">True to make the element visible; false to hide it.</param>
	public void SetHudElementVisible(HudElement element, bool visible) => GameAPI.Current.SetPlayerHudElementVisible(playerId.Value, element, visible);

	/// <summary>
	/// Selects the specified unit on this player's client, clearing previous selections.
	/// </summary>
	/// <param name="unit">The unit to select.</param>
	public void SelectUnit(UnitId unit) => GameAPI.Current.SelectPlayerUnit(playerId.Value, unit);

	/// <summary>
	/// Clears the unit selection on this player's client.
	/// </summary>
	public void ClearSelection() => GameAPI.Current.ClearPlayerSelection(playerId.Value);

	/// <summary>
	/// Retrieves a read-only span of units currently selected by this player.
	/// </summary>
	/// <returns>A read-only span of selected unit identifiers.</returns>
	public ReadOnlySpan<UnitId> GetSelectedUnits() => GameAPI.Current.GetPlayerSelectedUnits(playerId.Value);

	/// <summary>
	/// Displays floating feedback text on this player's screen with the specified RGB color.
	/// </summary>
	/// <param name="text">The message text.</param>
	/// <param name="color">The text color vector.</param>
	public void ShowFeedbackText(string text, Vector3 color) => GameAPI.Current.ShowPlayerFeedbackText(playerId.Value, text, color);

	/// <summary>
	/// Registers an ability with display metadata and execution behavior for this player's command card.
	/// </summary>
	/// <param name="abilityId">Unique identifier of the ability.</param>
	/// <param name="displayName">User-facing name of the ability.</param>
	/// <param name="tooltip">Detailed description displayed on hover.</param>
	/// <param name="iconPath">Resource path to the ability icon texture.</param>
	/// <param name="isInstant">True if the ability executes immediately without ground targeting.</param>
	public void RegisterAbility(string abilityId, string displayName, string tooltip, string iconPath = "", bool isInstant = false) =>
		GameAPI.Current.RegisterPlayerAbility(playerId.Value, abilityId, displayName, tooltip, iconPath, isInstant);

	/// <summary>
	/// Spawns a minimap ping on this player's client at the specified 3D world position.
	/// </summary>
	/// <param name="position">The coordinates of the ping.</param>
	public void PingMinimap(Vector3 position) => GameAPI.Current.PingPlayerMinimap(playerId.Value, position);

	/// <summary>
	/// Plays an audible warning sound effect on this player's client.
	/// </summary>
	public void PlayWarningSound() => GameAPI.Current.PlayPlayerWarningSound(playerId.Value);

	/// <summary>
	/// Plays an audible click UI sound effect on this player's client.
	/// </summary>
	public void PlayClickSound() => GameAPI.Current.PlayPlayerClickSound(playerId.Value);

	/// <summary>
	/// Begins interactive building placement on this player's client for the specified building type.
	/// </summary>
	/// <param name="buildingTypeId">The archetype identifier of the building to place.</param>
	public void StartBuildingPlacement(string buildingTypeId) => GameAPI.Current.StartPlayerBuildingPlacement(playerId.Value, buildingTypeId);

	/// <summary>
	/// Gets the language code preference of this player.
	/// </summary>
	public string Language => GameAPI.Current.GetPlayerLanguage(playerId.Value);

	/// <summary>
	/// Gets the hexadecimal color string for this player slot.
	/// </summary>
	public string ColorHex => GameAPI.Current.GetPlayerColorHex(playerId.Value);

	/// <summary>
	/// Gets the RGB color assigned to this player slot.
	/// </summary>
	public Vector3 Color => GameAPI.Current.GetPlayerColor(playerId.Value);

	/// <summary>
	/// Gets the formatted bbcode colored name of this player.
	/// </summary>
	public string ColoredName => GameAPI.Current.FormatColoredPlayerName(playerId.Value);

	/// <summary>
	/// Gets the display name of this player.
	/// </summary>
	/// <returns>The display name string.</returns>
	public string GetName() => playerId.Name;

	/// <summary>
	/// Gets the team index that this player belongs to.
	/// </summary>
	/// <returns>The team index.</returns>
	public int GetTeam() => playerId.Team;

	/// <summary>
	/// Sets the team index that this player belongs to.
	/// </summary>
	/// <param name="teamIndex">The new team index.</param>
	public void SetTeam(int teamIndex) => playerId.Team = teamIndex;

	/// <summary>
	/// Gets the start location for this player in 3D world space.
	/// </summary>
	/// <returns>The 3D start coordinates.</returns>
	public Vector3 GetStartLocation() => playerId.StartLocation;

	/// <summary>
	/// Gets the current gold resource amount for this player.
	/// </summary>
	/// <returns>The gold resource value.</returns>
	public float GetGold() => playerId.Gold;

	/// <summary>
	/// Sets the current gold resource amount for this player.
	/// </summary>
	/// <param name="amount">The new gold amount.</param>
	public void SetGold(float amount) => playerId.Gold = amount;

	/// <summary>
	/// Gets the current wood resource amount for this player.
	/// </summary>
	/// <returns>The wood resource value.</returns>
	public float GetWood() => playerId.Wood;

	/// <summary>
	/// Sets the current wood resource amount for this player.
	/// </summary>
	/// <param name="amount">The new wood amount.</param>
	public void SetWood(float amount) => playerId.Wood = amount;

	/// <summary>
	/// Gets the current stone resource amount for this player.
	/// </summary>
	/// <returns>The stone resource value.</returns>
	public float GetStone() => playerId.Stone;

	/// <summary>
	/// Sets the current stone resource amount for this player.
	/// </summary>
	/// <param name="amount">The new stone amount.</param>
	public void SetStone(float amount) => playerId.Stone = amount;

	/// <summary>
	/// Gets the current population count consumed by this player's living units.
	/// </summary>
	/// <returns>The current population count.</returns>
	public int GetCurrentPopulation() => playerId.CurrentPopulation;

	/// <summary>
	/// Gets the maximum population supply capacity for this player.
	/// </summary>
	/// <returns>The maximum population limit.</returns>
	public int GetMaxPopulation() => playerId.MaxPopulation;

	/// <summary>
	/// Sets the maximum population supply capacity for this player.
	/// </summary>
	/// <param name="max">The new maximum population capacity.</param>
	public void SetMaxPopulation(int max) => playerId.MaxPopulation = max;

	/// <summary>
	/// Gets the total number of enemy unit kills credited to this player.
	/// </summary>
	/// <returns>The total kill count.</returns>
	public int GetKills() => playerId.Kills;

	/// <summary>
	/// Sets the total number of enemy unit kills credited to this player.
	/// </summary>
	/// <param name="kills">The new kill count.</param>
	public void SetKills(int kills) => playerId.Kills = kills;
	}
}
