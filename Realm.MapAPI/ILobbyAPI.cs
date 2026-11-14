namespace Realm.MapAPI;

/// <summary>
///     The lobby a map script owns while it fills: which factions the map offers, which player slots are open, and what
///     faction, team and colour each player slot holds. It is handed to <see cref="ILobbyScript" /> while the authority sits
///     at tick zero, before any match exists.
/// </summary>
/// <remarks>
///     <para>
///         Reads are taken straight out of the shared roster the authority publishes, so they cost no host call and never
///         allocate. Writes are orders to the authority, which applies them to the lobby the players are sitting in, so a
///         write can be refused - a locked slot, a slot nobody holds, a faction the map does not offer, or a closed slot -
///         and reports whether it was accepted.
///     </para>
///     <para>
///         A lock is what a map offers a player as a choice it may not change: a script sets a slot's faction, team or
///         colour and locks it, and the lobby stops offering that field for that seat. A map that sets nothing leaves the
///         lobby's own assignment of colours and teams in place, which is what the default lobby rules do.
///     </para>
///     <para>
///         Every lobby carries the core game's three match settings - the shroud the board is played with, whether a team's
///         players sit together, and whether a late joiner waits before the board is described - and a map states the
///         values those settings start on. Everything else a match may be configured with belongs to the map, so a map
///         declares its own settings here, with the label, the kind of control and the range it wants the host offered, and
///         the host's choices are read back through <see cref="GetMatchSettingValue" />. The engine never learns what any of
///         those settings mean: a map that declares none offers its host the core settings alone.
///     </para>
///     <para>
///         A map states the values it starts on rather than reading them back where it can avoid it: a setting it declared
///         with a default is that default until the host changes it, and the value the match plays is read once when the
///         match starts.
///     </para>
/// </remarks>
public interface ILobbyAPI
{
	/// <summary>The number of slots the shared roster carries, which is every player index this API accepts.</summary>
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

	/// <summary>How many teams the lobby offers, which is the highest team number a slot may hold.</summary>
	int TeamCount { get; }

	/// <summary>
	///     States how many teams the lobby offers, which is what its players are offered as choices and what a slot's
	///     team may be numbered up to. A lobby whose map states nothing offers one team per player slot.
	/// </summary>
	/// <param name="teamCount">The number of teams, from one up to the seats this lobby may serve.</param>
	void SetTeamCount(int teamCount);

	/// <summary>
	///     Puts a player slot on a team. A slot whose team is locked, a slot nobody holds and a team number outside the
	///     lobby's teams are all refused.
	/// </summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="teamIndex">The team number, from 1 up to the lobby's teams, or 0 for no team.</param>
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
	///     Gives a player index the colour its units are drawn in, which is the colour its seat holds while it is seated
	///     and a colour the map keeps for an index nobody holds.
	/// </summary>
	/// <remarks>
	///     <para>
	///         A colour stated here is the map overriding the lobby's own assignment, so it is not held to the palette's
	///         one-player-per-colour rule: two indexes may be given the same colour, which is what a map that drives a
	///         player of its own beside a real one wants, so that both read as one side on screen. The lobby's own colour
	///         picker still refuses to hand a player a colour another player already holds - only this order may repeat one.
	///     </para>
	///     <para>
	///         An index nobody holds is not a seat the lobby can colour, so a colour stated for one is kept by the map
	///         rather than rejected: it is published to every screen beside the seats, and the match draws the units of
	///         that index in it. An index a player does hold changes that player's seat, and a seat whose colour the map
	///         locked is refused.
	///     </para>
	/// </remarks>
	/// <param name="playerIndex">The player index.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA.</param>
	/// <returns>Whether the authority took the order.</returns>
	bool SetPlayerColor(int playerIndex, uint colorRgba);

	/// <summary>Whether the slot's colour may no longer be changed.</summary>
	/// <param name="playerIndex">The player slot.</param>
	bool IsColorLocked(int playerIndex);

	/// <summary>
	///     Reads the colour a player index's units are drawn in on a minimap of one scheme, which is the colour the map
	///     stated for that scheme.
	/// </summary>
	/// <param name="playerIndex">The player index.</param>
	/// <param name="mode">Which of the minimap's two colourings the colour applies to.</param>
	/// <returns>
	///     The colour, packed as 0xRRGGBBAA, or zero while the map stated none for that scheme and the scheme decides the
	///     colour itself.
	/// </returns>
	uint GetPlayerMinimapColor(int playerIndex, Realm.Ecs.MinimapColorMode mode);

	/// <summary>
	///     States the colour a player index's units are drawn in on a minimap of one scheme, which is how a map says that a
	///     player it drives reads as part of a side on one of the minimap's two readings without changing the colour its
	///     units are painted in the world.
	/// </summary>
	/// <remarks>
	///     The two schemes are independent: a colour stated for <see cref="Realm.Ecs.MinimapColorMode.TeamColors" /> leaves
	///     what the same index looks like under <see cref="Realm.Ecs.MinimapColorMode.PlayerColors" /> alone, and the other
	///     way round. A colour of zero clears the statement, which returns that index to the colour it would be drawn in
	///     without one, and an index the lobby does not carry is refused.
	/// </remarks>
	/// <param name="playerIndex">The player index.</param>
	/// <param name="mode">Which of the minimap's two colourings the colour applies to.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA, or zero to clear the statement.</param>
	/// <returns>Whether the authority took the order.</returns>
	bool SetPlayerMinimapColor(int playerIndex, Realm.Ecs.MinimapColorMode mode, uint colorRgba);

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
	///     Whether the slot is open to a player. A seat is only ever placed in an open slot, and a closed slot is one the
	///     host removed from the board the lobby offers while keeping the row on screen.
	/// </summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <returns>Whether the slot is open.</returns>
	bool IsSlotOpen(int playerIndex);

	/// <summary>
	///     Opens or closes one player slot, which is how a map states the seats its board is authored for: a lobby whose
	///     map closes nothing offers every slot it has. A slot somebody already holds stays occupied either way, and the
	///     order is refused for a slot outside the roster.
	/// </summary>
	/// <param name="playerIndex">The player slot.</param>
	/// <param name="open">Whether the slot accepts a player.</param>
	/// <returns>Whether the authority took the order.</returns>
	bool SetSlotOpen(int playerIndex, bool open);

	/// <summary>
	///     Opens the first slots up to a count and closes the rest, which is the short way for a map to state the number
	///     of players its board seats. A slot somebody already holds is not closed by this order.
	/// </summary>
	/// <param name="count">How many slots, counted from the first, the lobby should offer.</param>
	/// <returns>Whether the authority took the order.</returns>
	bool SetOpenSlotCount(int count);

	/// <summary>
	///     States the most players this map will seat: every player index above that count belongs to the map alone, and no
	///     player - and no host opening a row - may be placed in one.
	/// </summary>
	/// <remarks>
	///     <para>
	///         A map that drives players of its own needs indexes no person can take, or a player joining at the wrong
	///         moment would sit in the seat the map's own rules are about to use. This is the order that reserves them: an
	///         index above the count stays closed however the lobby is run, the host may not reopen it, and nobody is
	///         admitted into it.
	///     </para>
	///     <para>
	///         A count stated here also caps <see cref="SetOpenSlotCount" />, so a map states one number for the players it
	///         seats and reserves everything after it. A map that states no limit leaves every slot the lobby carries
	///         available to its host, which is the neutral answer for a board with no players of its own.
	///     </para>
	/// </remarks>
	/// <param name="maximumPlayers">The highest player index a person may be seated in.</param>
	/// <returns>Whether the authority took the order.</returns>
	bool SetPlayerSlotLimit(int maximumPlayers);

	/// <summary>
	///     Declares one match setting of the map's own, which is how a map offers its host a rule the core game has no
	///     concept of - how many building options a draw offers, whether every player drafts the same deck, how much of
	///     its board a map seats. The authority keeps the declaration and publishes it to every screen in the lobby, and
	///     the value of it is read back through <see cref="GetMatchSettingValue" />. Declaring a setting the lobby already
	///     holds is how a map restates it after changing its wording, and keeps the value the host chose.
	/// </summary>
	/// <param name="settingId">
	///     The identifier the map declares the setting under. It is the map's own number and never leaves the map, so any
	///     positive value it likes will do; two settings may not share one.
	/// </param>
	/// <param name="control">How the host edits the setting.</param>
	/// <param name="label">The label the host reads, at most 48 characters.</param>
	/// <param name="tooltip">The sentence that explains the setting, or empty for none, at most 200 characters.</param>
	/// <param name="defaultValue">The value the setting holds until the host changes it.</param>
	/// <returns>Whether the authority holds the declaration afterwards.</returns>
	bool AddMatchSetting(int settingId, Realm.Ecs.LobbySettingControl control, string label, string tooltip, int defaultValue);

	/// <summary>
	///     Adds one option to a setting declared as a choice. The option's index is the value the setting holds while it
	///     is chosen, so the first option added is zero.
	/// </summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="optionLabel">The text the host reads in the dropdown, at most 48 characters.</param>
	/// <returns>Whether the option is part of the setting afterwards.</returns>
	bool AddMatchSettingOption(int settingId, string optionLabel);

	/// <summary>
	///     States the values a count setting may hold. A count declared without a range offers zero to one.
	/// </summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="minimum">The lowest value the host may choose.</param>
	/// <param name="maximum">The highest value the host may choose.</param>
	/// <returns>Whether the authority holds the range afterwards.</returns>
	bool SetMatchSettingRange(int settingId, int minimum, int maximum);

	/// <summary>
	///     Reads the value the host chose for one of the map's own settings, which is what a map reads to act on it. A
	///     setting the map never declared, or one the lobby holds no value for, answers zero.
	/// </summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <returns>The value the setting holds.</returns>
	int GetMatchSettingValue(int settingId);

	/// <summary>
	///     States whether the lobby's Teams Together checkbox starts checked, which is what a map that plays its teams as
	///     sides states: a checked lobby groups every team's players into consecutive player slots when the match starts, so
	///     a team's members sit next to each other rather than wherever they happened to join. The host may still clear the
	///     checkbox, and a host that has changed it keeps its own choice rather than being overruled by this order.
	/// </summary>
	/// <param name="teamsTogether">Whether the checkbox starts checked.</param>
	void SetTeamsTogetherDefault(bool teamsTogether);

	/// <summary>
	///     States whether a player who joins after the match has started waits for a delay before the board is described to
	///     them. The core game starts every lobby with the delay on; the host may still switch it off, and a value stated here
	///     is the answer the lobby returns to when the host clears theirs.
	/// </summary>
	/// <param name="enabled">Whether late joiners wait before they see the board.</param>
	void SetSpectatorDelayDefault(bool enabled);

	/// <summary>
	///     States which shroud the lobby starts with, which is how a map says whether its board is played with the whole
	///     thing visible or with vision limited to what each player's units can see. The host may still choose another mode
	///     for the match it starts, and a mode stated here is the answer the lobby returns to when the host clears theirs.
	/// </summary>
	/// <param name="shroudMode">The mode the lobby starts on.</param>
	void SetShroudModeDefault(Realm.Ecs.MatchShroudMode shroudMode);

	/// <summary>
	///     Assigns a board coordinate to a team, which is how a map that seats its teams apart states where each side
	///     begins: every player of that team starts at a random point inside the coordinate rather than at the anchor the
	///     board states for that player slot. An assignment replaces the board's own anchors for that team's players, so a
	///     team whose members share ground needs nothing from the board's per-slot anchors, and a team with no assignment
	///     keeps them. A name the board does not author is refused, because a team seated nowhere is a team the match cannot
	///     start; an empty name clears the team's assignment.
	/// </summary>
	/// <param name="teamIndex">The team number, from one up to the teams this lobby offers.</param>
	/// <param name="coordinateName">The board coordinate the team begins inside, or empty to clear the assignment.</param>
	/// <returns>Whether the authority took the order.</returns>
	bool SetTeamSpawnCoordinate(int teamIndex, string coordinateName);
}
