namespace Realm.MapAPI;

/// <summary>
///     The roster a map script owns while its lobby is filling: which factions the map offers, how many players it seats,
///     and what faction, team and colour each player slot holds. It is handed to <see cref="ILobbyScript" /> while the
///     authority sits at tick zero, before any match exists.
/// </summary>
/// <remarks>
///     <para>
///         Reads are taken straight out of the shared roster the authority publishes, so they cost no host call and never
///         allocate. Writes are orders to the authority, which applies them to the lobby the players are sitting in, so a
///         write can be refused - a locked slot, a slot nobody holds, a faction the map does not offer, or a full lobby -
///         and reports whether it was accepted.
///     </para>
///     <para>
///         A lock is what a map offers a player as a choice it may not change: a script sets a slot's faction, team or
///         colour and locks it, and the lobby stops offering that field for that seat. A map that sets nothing leaves the
///         lobby's own assignment of colours and teams in place, which is what the default lobby rules do.
///     </para>
/// </remarks>
public interface ILobbyAPI
{
	/// <summary>How many player slots this lobby currently seats. Slots are indexed from one; slot zero is not a player.</summary>
	int MaximumPlayerCount { get; }

	/// <summary>The number of slots the shared roster carries, which is the highest player index this API accepts.</summary>
	int SlotCount { get; }

	/// <summary>How many factions the map offers, in the order <see cref="GetFactionName" /> reports them.</summary>
	int FactionCount { get; }

	/// <summary>
	///     The name of one of the map's factions without allocating a string, so a script may read it inside its lobby
	///     tick. The span is empty for an index this lobby does not offer.
	/// </summary>
	/// <param name="factionIndex">The faction's position in the lobby's faction table.</param>
	/// <returns>The faction's name, valid until the authority restates the roster.</returns>
	ReadOnlySpan<char> GetFactionName(int factionIndex);

	/// <summary>
	///     Adds a faction to the lobby's roster, which is how a map states the choices its players are offered. The
	///     authority keeps the table, and a name the roster already offers is not added twice.
	/// </summary>
	/// <param name="factionName">The faction's name, at most 24 characters.</param>
	/// <returns>Whether the faction is part of the roster afterwards.</returns>
	bool AddFaction(string factionName);

	/// <summary>The faction table position of the slot's faction, or -1 when the slot holds no faction.</summary>
	/// <param name="playerIndex">The player slot.</param>
	int GetPlayerFactionIndex(int playerIndex);

	/// <summary>The faction the slot plays as, without allocating a string. Empty when the slot holds no faction.</summary>
	/// <param name="playerIndex">The player slot.</param>
	ReadOnlySpan<char> GetPlayerFaction(int playerIndex);

	/// <summary>
	///     Gives a player slot a faction from the lobby's roster. A slot whose faction is locked, a slot nobody holds and
	///     a faction the roster does not offer are all refused.
	/// </summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="factionName">The faction the slot should play as.</param>
	/// <returns>Whether the authority took the order.</returns>
	bool SetPlayerFaction(int playerIndex, string factionName);

	/// <summary>Whether the slot's faction may no longer be changed.</summary>
	/// <param name="playerIndex">The player slot.</param>
	bool IsFactionLocked(int playerIndex);

	/// <summary>Locks or unlocks the slot's faction, which is what stops a player changing it in the lobby.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="locked">Whether the field is locked.</param>
	void SetFactionLocked(int playerIndex, bool locked);

	/// <summary>The team number the slot plays on, where 0 means it plays on no team.</summary>
	/// <param name="playerIndex">The player slot.</param>
	int GetPlayerTeam(int playerIndex);

	/// <summary>
	///     Puts a player slot on a team. A slot whose team is locked, a slot nobody holds and a team number outside the
	///     lobby's seats are all refused.
	/// </summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="teamIndex">The team number, from 1 up to the lobby's seats, or 0 for no team.</param>
	/// <returns>Whether the authority took the order.</returns>
	bool SetPlayerTeam(int playerIndex, int teamIndex);

	/// <summary>Whether the slot's team may no longer be changed.</summary>
	/// <param name="playerIndex">The player slot.</param>
	bool IsTeamLocked(int playerIndex);

	/// <summary>Locks or unlocks the slot's team.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="locked">Whether the field is locked.</param>
	void SetTeamLocked(int playerIndex, bool locked);

	/// <summary>The slot's colour, packed as 0xRRGGBBAA the way the lobby publishes its seats.</summary>
	/// <param name="playerIndex">The player slot.</param>
	uint GetPlayerColor(int playerIndex);

	/// <summary>
	///     Gives a player slot a colour. A slot whose colour is locked and a slot nobody holds are both refused.
	/// </summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA.</param>
	/// <returns>Whether the authority took the order.</returns>
	bool SetPlayerColor(int playerIndex, uint colorRgba);

	/// <summary>Whether the slot's colour may no longer be changed.</summary>
	/// <param name="playerIndex">The player slot.</param>
	bool IsColorLocked(int playerIndex);

	/// <summary>Locks or unlocks the slot's colour.</summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="locked">Whether the field is locked.</param>
	void SetColorLocked(int playerIndex, bool locked);

	/// <summary>Whether a player or a computer holds the slot.</summary>
	/// <param name="playerIndex">The player slot.</param>
	bool IsOccupied(int playerIndex);

	/// <summary>Whether the slot is held by a computer rather than by a person.</summary>
	/// <param name="playerIndex">The player slot.</param>
	bool IsComputer(int playerIndex);

	/// <summary>Whether the slot belongs to the player administering the lobby.</summary>
	/// <param name="playerIndex">The player slot.</param>
	bool IsHost(int playerIndex);

	/// <summary>
	///     Sets how many players the lobby seats. The authority clamps it to the seats its map may serve and refuses a
	///     count smaller than the players already sitting in it.
	/// </summary>
	/// <param name="maximumPlayerCount">The number of player slots the lobby should seat.</param>
	void SetMaximumPlayerCount(int maximumPlayerCount);
}
