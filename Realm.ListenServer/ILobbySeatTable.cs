using Realm.Ecs;
using Realm.Shared.Network;

namespace Realm.ListenServer;

/// <summary>
///     The seats a map's lobby rules apply to: the roster a script reads while it states what its lobby offers, and the
///     seats its faction, team, colour and seat-count orders land on. An open lobby holds them for the players sitting in
///     it; a match started without a lobby holds the seats it was handed for the one pass its map's rules take.
/// </summary>
public interface ILobbySeatTable
{
	/// <summary>How many player slots this table seats.</summary>
	int MaximumPlayers { get; }

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

	/// <summary>Seats a different number of players, which is what a map's roster states.</summary>
	/// <param name="maximumPlayerCount">The number of player slots to seat.</param>
	/// <returns>Whether the table seats that many players afterwards.</returns>
	bool TrySetMaximumPlayers(int maximumPlayerCount);

	/// <summary>Hides one match setting from the lobby's host, or shows it again.</summary>
	/// <param name="setting">The setting being hidden or shown.</param>
	/// <param name="hidden">Whether the setting is hidden.</param>
	void SetSettingHidden(LobbySetting setting, bool hidden);

	/// <summary>States whether late joiners wait before the board is described to them.</summary>
	/// <param name="enabled">Whether the delay is on.</param>
	void SetSpectatorDelayDefault(bool enabled);
}
