using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

/// <summary>ready flags, seat updates and the locked-field helpers.</summary>
public partial class ServerLobby
	{
	private void SetReady(LobbySeat sender, bool ready)
	{
		if (sender.IsComputer) return;

		sender.IsReady = ready;
		_seats[sender.Slot] = sender;
		BroadcastState();
	}

	private void UpdateSeat(int peerId, bool isAdmin, LobbySeat sender, LobbyCommand command)
	{
		int targetPeerId = command.TargetPeerId != 0 ? command.TargetPeerId : peerId;
		if (!isAdmin && targetPeerId != peerId) return;

		if (!TryFindSeat(targetPeerId, out LobbySeat target))
		{
			_log.Write($"peer {peerId} asked to change a seat this lobby does not hold (target {targetPeerId}); the command is refused");
			return;
		}

		string name = LobbyLimits.SanitizeDisplayName(command.Name);
		if (!string.IsNullOrEmpty(name)) target.Name = MakeNameDistinct(name, target.Slot);

		string faction = LobbyLimits.Sanitize(command.Faction, LobbyLimits.MaximumFactionLength);
		if (!string.IsNullOrEmpty(faction) && !IsFactionLocked(target.Slot))
		{
			target.Faction = faction;
		}

		if (LobbyLimits.IsAcceptableTeamName(command.TeamName) && !IsTeamLocked(target.Slot))
		{
			string teamName = LobbyLimits.Sanitize(command.TeamName, LobbyLimits.MaximumTeamNameLength);
			int team = ResolveTeamNumber(teamName, target.Slot);
			if (CanSeatPlayTeam(target.Slot, team, target.Team))
			{
				target.TeamName = teamName;
				target.Team = team;
				if (team == 0) target.IsReady = false;
			}
			else
			{
				_log.Write($"seat {target.Slot} was offered team '{teamName}', which needs a free player seat this lobby does not have; the order is refused");
			}
		}

		if (command.ColorRgba != 0 && !IsColorLocked(target.Slot) && !IsColorHeldByAnotherSeat(target.Slot, command.ColorRgba)) target.ColorRgba = command.ColorRgba;

		_seats[target.Slot] = target;
		if (target.PeerId > 0) _slotByPeer[target.PeerId] = target.Slot;
		BroadcastState();
	}

	private bool IsFactionLocked(int slot) => Roster?.IsFactionLocked(slot) ?? false;

	private bool IsTeamLocked(int slot) => Roster?.IsTeamLocked(slot) ?? false;

	private bool IsColorLocked(int slot) => Roster?.IsColorLocked(slot) ?? false;
}
