// Extracted from IGameAPI.cs (task 6 decomposition).
using System.Numerics;


namespace Realm.MapAPI;


/// <summary>
/// Defines the safe, public API exposed to map scripts to interact with the game engine.
/// </summary>
public partial interface IGameAPI
{
	/// <summary>
	/// Gets the total number of player slots in the current session (including empty slots).
	/// </summary>
	int PlayerCount { get; }

	/// <summary>
	/// Returns the display name of the player occupying the specified zero-based slot index.
	/// Returns an empty string if the slot is not occupied.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	string GetPlayerName(int playerIndex);

	/// <summary>
	/// Sets the gold resource for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="amount">The new gold amount.</param>
	void SetPlayerGold(int playerIndex, float amount);

	/// <summary>
	/// Adds the specified amount to the gold resource for the given player slot.
	/// Negative values subtract gold.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="delta">The amount to add (may be negative).</param>
	void AdjustPlayerGold(int playerIndex, float delta);

	/// <summary>
	/// Spawns a unit owned by the specified player slot at the given position.
	/// </summary>
	/// <param name="unitTypeId">The type identifier of the unit to spawn.</param>
	/// <param name="position">The spawn coordinates in 2D ground space (X, Z).</param>
	/// <param name="playerIndex">Zero-based player slot index that will own the unit.</param>
	/// <param name="executeSpawnShader">True to play the spawn shader effect if configured, false to skip.</param>
	/// <param name="bypassPopulation">True to bypass population cap limits when spawning, false to enforce population limits.</param>
	/// <returns>The spawned unit identifier.</returns>
	UnitId SpawnUnitForPlayer(string unitTypeId, Vector2 position, int playerIndex, bool executeSpawnShader = true, bool bypassPopulation = false);

	/// <summary>
	/// Retrieves all alive units owned by the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>A read-only span of units belonging to that player.</returns>
	ReadOnlySpan<UnitId> GetUnitsOwnedByPlayer(int playerIndex);

	/// <summary>
	/// Retrieves the units a player owns that have nothing to do: no move order, no attack-move order, no attack target
	/// and no gathering trip. It is the set the engine's own idle-unit selection offers, so a map that sweeps idle units
	/// agrees with the player who asks for them.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>A read-only span of that player's idle units.</returns>
	ReadOnlySpan<UnitId> GetIdleUnitsOwnedByPlayer(int playerIndex);

	/// <summary>
	/// Triggers the defeat condition for the specified player slot, playing the defeat sequence only for that player.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="reason">An optional human-readable reason string shown to the player.</param>
	void TriggerPlayerDefeat(int playerIndex, string reason = "");

	/// <summary>
	/// Triggers the victory condition for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	void TriggerPlayerVictory(int playerIndex);

	/// <summary>
	/// Broadcasts a text message to all players simultaneously.
	/// </summary>
	/// <param name="message">The message string to display.</param>
	void BroadcastMessage(string message);

	/// <summary>
	/// Sends a text message to a single player slot only.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="message">The message string to display.</param>
	void SendMessageToPlayer(int playerIndex, string message);

	/// <summary>
	/// Triggered when a scheduled timer expires.
	/// </summary>
	event Action<int>? OnTimerExpired;

	/// <summary>
	/// Schedules a one-shot callback to fire after the specified delay (in seconds).
	/// </summary>
	/// <param name="delay">Delay in seconds before the callback is invoked.</param>
	/// <returns>A handle that can be passed to <see cref="CancelTimer"/> to cancel the timer before it fires.</returns>
	int ScheduleTimer(float delay);

	/// <summary>
	/// Schedules a repeating callback to fire every <paramref name="interval"/> seconds.
	/// </summary>
	/// <param name="interval">Interval in seconds between invocations.</param>
	/// <returns>A handle that can be passed to <see cref="CancelTimer"/> to stop the repeating timer.</returns>
	int ScheduleRepeatingTimer(float interval);

	/// <summary>
	/// Schedules a one-shot callback to fire after the specified delay (in seconds).
	/// </summary>
	int ScheduleTimer(float delay, Action callback)
	{
		int handle = ScheduleTimer(delay);
		Action<int>? handler = null;
		handler = (h) =>
		{
			if (h == handle)
			{
				callback();
				OnTimerExpired -= handler;
			}
		};
		OnTimerExpired += handler;
		return handle;
	}

	/// <summary>
	/// Schedules a repeating callback to fire every <paramref name="interval"/> seconds.
	/// </summary>
	int ScheduleRepeatingTimer(float interval, Action callback)
	{
		int handle = ScheduleRepeatingTimer(interval);
		OnTimerExpired += (h) =>
		{
			if (h == handle) callback();
		};
		return handle;
	}

	/// <summary>
	/// Cancels a previously scheduled timer, preventing any future invocations.
	/// </summary>
	/// <param name="timerHandle">The handle returned by <see cref="ScheduleTimer(float)"/> or <see cref="ScheduleRepeatingTimer(float)"/>.</param>
	void CancelTimer(int timerHandle);

	/// <summary>
	/// Returns a uniformly distributed random integer in the inclusive range [<paramref name="min"/>, <paramref name="max"/>].
	/// </summary>
	/// <remarks>
/// The value is drawn from the deterministic stream shared by the host simulation and the map script. Map scripts must use this method instead of
/// <see cref="System.Random"/> so that the authority's world and the script acting on it draw the same sequence for the same match seed. Call it
/// exactly once per decision and in the same order, otherwise the shared sequence advances differently.
	/// </remarks>
	/// <param name="min">Inclusive lower bound.</param>
	/// <param name="max">Inclusive upper bound.</param>
	int RandomInt(int min, int max);

	/// <summary>
	/// Returns a uniformly distributed random float in the range [<paramref name="min"/>, <paramref name="max"/>].
	/// </summary>
	/// <remarks>
/// The value is drawn from the deterministic stream shared by the host simulation and the map script. Map scripts must use this method instead of
/// <see cref="System.Random"/> so that the authority's world and the script acting on it draw the same sequence for the same match seed. Call it
/// exactly once per decision and in the same order, otherwise the shared sequence advances differently.
	/// </remarks>
	/// <param name="min">Inclusive lower bound.</param>
	/// <param name="max">Inclusive upper bound.</param>
	float RandomFloat(float min, float max);


	/// <summary>
	/// Returns the start location for the specified player slot in 3D world space.
	/// The Y component represents terrain height at that position.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>The start position in 3D world space, or <see cref="Vector3.Zero"/> if the slot has no start location.</returns>
	Vector3 GetPlayerStartLocation(int playerIndex);

	/// <summary>
	/// Assigns the specified player slot to a numbered team.
	/// Players on the same team are treated as allies.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="teamIndex">Zero-based team index.</param>
	void SetPlayerTeam(int playerIndex, int teamIndex);

	/// <summary>
	/// Configures whether two player slots treat each other as passive allies
	/// (shared vision, units do not attack each other).
	/// </summary>
	/// <param name="playerIndex">Zero-based index of the first player.</param>
	/// <param name="otherPlayerIndex">Zero-based index of the second player.</param>
	/// <param name="allied">True to make them allied; false to revert to enemies.</param>
	void SetPlayersAllied(int playerIndex, int otherPlayerIndex, bool allied);

	/// <summary>
	/// Returns true if two player slots treat each other as passive allies.
	/// </summary>
	/// <param name="playerIndex">Index of the first player.</param>
	/// <param name="otherPlayerIndex">Index of the second player.</param>
	bool ArePlayersAllied(int playerIndex, int otherPlayerIndex);

	/// <summary>
	/// Sets whether the specified player slot is active in the game session.
	/// </summary>
	/// <param name="playerIndex">Player slot index.</param>
	/// <param name="active">True if active, false otherwise.</param>
	void SetPlayerActive(int playerIndex, bool active);

	/// <summary>
	/// Kicks the specified player slot from the match.
	/// </summary>
	/// <param name="playerIndex">Player slot index.</param>
	/// <param name="reason">Optional reason message for the kick.</param>
	void KickPlayer(int playerIndex, string reason = "Kicked by host");

	/// <summary>
	/// Adds or configures an AI computer player in the specified slot.
	/// </summary>
	/// <param name="playerIndex">The player slot index.</param>
	/// <param name="name">Optional display name for the bot.</param>
	/// <param name="teamIndex">Optional team index (default -1 for unassigned).</param>
	/// <returns>True if successfully added or configured.</returns>
	bool AddComputerPlayer(int playerIndex, string? name = null, int teamIndex = -1);

	/// <summary>
	/// Sets whether the specified player slot is treated as a proxy (AI-controlled
	/// stand-in that mirrors another player's drafted units in combat).
	/// Proxy slots can be given computer-controller behaviour independent of the real player.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="isProxy">True to enable proxy/computer control; false for user control.</param>
	void SetPlayerComputerControlled(int playerIndex, bool isProxy);

	/// <summary>
	/// Sets the wood (lumber) resource for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="amount">The new wood amount.</param>
	void SetPlayerWood(int playerIndex, float amount);

	/// <summary>
	/// Adds the specified amount to the wood resource for the given player slot.
	/// Negative values subtract wood.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="delta">The amount to add (may be negative).</param>
	void AdjustPlayerWood(int playerIndex, float delta);

	/// <summary>
	/// Sets the maximum population capacity for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="max">The new population cap.</param>
	void SetPlayerMaxPopulation(int playerIndex, int max);

	/// <summary>
	/// Orders all units owned by the specified player slot to attack-move to the destination.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="destination">The destination in world coordinates.</param>
	void IssueAttackMoveOrderToPlayer(int playerIndex, Vector3 destination);

	/// <summary>
	/// Returns the number of units owned by the specified player.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	int CountUnitsOwnedByPlayer(int playerIndex);

	/// <summary>
	/// Retrieves a <see cref="PlayerId"/> identifier for the specified zero-based player slot index,
	/// providing fluent zero-allocation access to player state and actions.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>A <see cref="PlayerId"/> bound to the specified player slot.</returns>
	PlayerId GetPlayer(int playerIndex) => new(playerIndex);

	/// <summary>
	/// Retrieves all active player slot identifiers in the current session.
	/// </summary>
	/// <returns>A read-only span of active <see cref="PlayerId"/> identifiers in ascending slot order.</returns>
	ReadOnlySpan<PlayerId> GetActivePlayers();

	/// <summary>
	/// Returns the zero-based slot indices of all active players.
	/// </summary>
	/// <returns>An enumerable sequence of active player slot indices.</returns>
	IEnumerable<int> GetActivePlayerIndices()
	{
		var players = GetActivePlayers();
		var result = new List<int>(players.Length);
		for (int i = 0; i < players.Length; i++)
		{
			result.Add(players[i].Value);
		}
		return result;
	}

	/// <summary>
	/// Sets the stone resource for the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="amount">The new stone amount.</param>
	void SetPlayerStone(int playerIndex, float amount);

	/// <summary>
	/// Adds the specified amount to the stone resource for the given player slot.
	/// Negative values subtract stone.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <param name="delta">The amount to add (may be negative).</param>
	void AdjustPlayerStone(int playerIndex, float delta);

	/// <summary>
	/// Returns the standard hex color code string (RRGGBB) corresponding to the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>A 6-character hex color string.</returns>
	string GetPlayerColorHex(int playerIndex);

	/// <summary>
	/// Returns the RGB color assigned to the specified player slot.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>The player's color in RGB format.</returns>
	Vector3 GetPlayerColor(int playerIndex);

	/// <summary>
	/// Returns the player name formatted with rich text / BBCode markup matching their assigned player slot color.
	/// </summary>
	/// <param name="playerIndex">Zero-based player slot index.</param>
	/// <returns>The color-formatted player name string.</returns>
	string FormatColoredPlayerName(int playerIndex)
	{
		string name = GetPlayerName(playerIndex);
		string color = GetPlayerColorHex(playerIndex);
		return $"[color=#{color}]{name}[/color]";
	}
}
