using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The seats a map's lobby rules apply to: the roster a script reads while it states what its lobby offers, and the
///     seats its faction, team, colour, slot and setting orders land on. An open lobby holds them for the players sitting
///     in it; a match started without a lobby holds the seats it was handed for the one pass its map's rules take.
/// </summary>
public interface ILobbySeatTable
{
	/// <summary>How many player slots this table leaves open to a player.</summary>
	int OpenSlotCount { get; }

	/// <summary>Answers whether one player slot accepts a player.</summary>
	/// <param name="slot">The player slot.</param>
	/// <returns>Whether the slot is open.</returns>
	bool IsSlotOpen(int slot);

	/// <summary>Answers the seat one slot holds.</summary>
	/// <param name="slot">The seat's slot.</param>
	/// <param name="seat">The seat that slot holds, when one does.</param>
	/// <returns>Whether the slot is seated.</returns>
	bool TryGetSeatBySlot(int slot, out LobbySeat seat);

	/// <summary>States the table to whoever is watching it, which an open lobby's screens are and a started match is not.</summary>
	void BroadcastState();

	/// <summary>Gives a seat a faction from the roster.</summary>
	/// <param name="slot">The seat's slot.</param>
	/// <param name="faction">The faction the seat plays as.</param>
	/// <returns>Whether the seat holds that faction afterwards.</returns>
	bool ApplyRosterFaction(int slot, string faction);

	/// <summary>Puts a seat on a team its map's roster chose.</summary>
	/// <param name="slot">The seat's slot.</param>
	/// <param name="team">The team number, where zero means it plays on no team.</param>
	/// <returns>Whether the seat holds that team afterwards.</returns>
	bool ApplyRosterTeam(int slot, int team);

	/// <summary>Gives a seat a colour its map's roster chose, or keeps one for a slot nobody holds.</summary>
	/// <param name="slot">The seat's slot.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA.</param>
	/// <returns>Whether the slot holds that colour afterwards.</returns>
	bool ApplyRosterColor(int slot, uint colorRgba);

	/// <summary>Reads the colour the table keeps for a slot nobody holds, which is what the roster publishes for it.</summary>
	/// <param name="slot">The player slot.</param>
	/// <returns>The colour, packed as 0xRRGGBBAA, or zero while the table keeps none.</returns>
	uint ColorOverrideOf(int slot);

	/// <summary>Reads the minimap colour the table holds for one scheme.</summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="mode">Which of the minimap's two colourings the colour applies to.</param>
	/// <returns>The colour, packed as 0xRRGGBBAA, or zero while the table holds none.</returns>
	uint MinimapColorOf(int slot, MinimapColorMode mode);

	/// <summary>States the colour a slot's units are drawn in on one minimap scheme, or clears the statement.</summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="mode">Which of the minimap's two colourings the colour applies to.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA, or zero to clear the statement.</param>
	/// <returns>Whether the table holds that statement afterwards.</returns>
	bool ApplyRosterMinimapColor(int slot, MinimapColorMode mode, uint colorRgba);

	/// <summary>States that one player index stands in for another, which is how a map says a player it drives itself carries a real player's army.</summary>
	/// <param name="slot">The player index standing in for another.</param>
	/// <param name="ownerSlot">The player index it stands in for, or zero to clear the statement.</param>
	/// <returns>Whether the table holds that statement afterwards.</returns>
	bool ApplyRosterProxyOwner(int slot, int ownerSlot);

	/// <summary>Reads the player index one player index stands in for.</summary>
	/// <param name="slot">The player index.</param>
	/// <returns>The player index it stands in for, or zero while it stands in for nobody.</returns>
	int ProxyOwnerOf(int slot);

	/// <summary>Keeps every player index above a count for the map itself.</summary>
	/// <param name="maximumPlayers">The highest player index a person may be seated in.</param>
	/// <returns>Whether the table holds that limit afterwards.</returns>
	bool TrySetPlayerSlotLimit(int maximumPlayers);

	/// <summary>Opens or closes one player slot, which is what a map's roster states.</summary>
	/// <param name="slot">The player slot.</param>
	/// <param name="open">Whether the slot accepts a player.</param>
	/// <returns>Whether the table offers that slot afterwards.</returns>
	bool TrySetSlotOpen(int slot, bool open);

	/// <summary>Opens the first slots up to a count and closes the rest, which is the short form of a map's slot order.</summary>
	/// <param name="count">How many slots, counted from the first, the table should offer.</param>
	/// <returns>Whether the table offers those slots afterwards.</returns>
	bool TrySetOpenSlotCount(int count);

	/// <summary>States whether late joiners wait before the board is described to them.</summary>
	/// <param name="enabled">Whether the delay is on.</param>
	void SetSpectatorDelayDefault(bool enabled);

	/// <summary>Declares one match setting of the map's own, which is what its host is then offered.</summary>
	/// <param name="settingId">The identifier the map declares the setting under.</param>
	/// <param name="control">How the host edits the setting.</param>
	/// <param name="label">The label the host reads.</param>
	/// <param name="tooltip">The sentence that explains the setting, or empty for none.</param>
	/// <param name="defaultValue">The value the setting holds until the host changes it.</param>
	void DeclareMatchSetting(int settingId, LobbySettingControl control, string label, string tooltip, int defaultValue);

	/// <summary>Adds one option to a setting declared as a choice.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="optionLabel">The text the host reads in the dropdown.</param>
	void DeclareMatchSettingOption(int settingId, string optionLabel);

	/// <summary>States the values a count setting may hold.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <param name="minimum">The lowest value the host may choose.</param>
	/// <param name="maximum">The highest value the host may choose.</param>
	void DeclareMatchSettingRange(int settingId, int minimum, int maximum);

	/// <summary>Reads the value one declared setting holds.</summary>
	/// <param name="settingId">The identifier the map declared the setting under.</param>
	/// <returns>The value the setting holds, or zero when the table holds none.</returns>
	int GetMatchSettingValue(int settingId);
}
