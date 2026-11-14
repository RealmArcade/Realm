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

	/// <summary>Gives a seat a colour its map's roster chose.</summary>
	/// <param name="slot">The seat's slot.</param>
	/// <param name="colorRgba">The colour, packed as 0xRRGGBBAA.</param>
	/// <returns>Whether the seat holds that colour afterwards.</returns>
	bool ApplyRosterColor(int slot, uint colorRgba);

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
