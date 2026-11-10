using System.Numerics;
using System.Security.Cryptography;
using MemoryPack;
using Realm.Shared.Distribution;
using Realm.Shared.Maps;
using Realm.Shared.Network;
using Realm.Shared.Players;
using Realm.Shared.Services;

namespace Realm.ListenServer;

/// <summary>admin checks and the slot, team, colour and name defaults.</summary>
public partial class ServerLobby
	{
	private bool RequireAdmin(int peerId, bool isAdmin, out int slot, string what)
	{
		slot = 0;
		if (isAdmin) return true;

		_log.Write($"peer {peerId} tried to {what} without administering the lobby; the command is refused");
		return false;
	}

	private bool TryGetComputerSeat(int seatPeerId, out LobbySeat seat)
	{
		foreach (LobbySeat candidate in _seats.Values)
		{
			if (!candidate.IsComputer || candidate.PeerId != seatPeerId) continue;
			seat = candidate;
			return true;
		}

		seat = default;
		return false;
	}

	private int FindFreePlayingSlot(int preferredSlot)
	{
		if (PlayingSeatCount >= _maximumPlayers) return -1;
		if (preferredSlot > 0 && preferredSlot <= _maximumPlayers && !_seats.ContainsKey(preferredSlot)) return preferredSlot;

		for (int slot = 1; slot <= _maximumPlayers; slot++)
		{
			if (!_seats.ContainsKey(slot)) return slot;
		}

		return -1;
	}

	/// <summary>
	///     Finds a seat a spectator may hold. Spectators sit outside the player seats, so the seats above the player count
	///     are offered first and the player seats are left for players.
	/// </summary>
	private int FindFreeSpectatorSlot()
	{
		for (int slot = _maximumPlayers + 1; slot <= LobbyLimits.MaximumSeats; slot++)
		{
			if (!_seats.ContainsKey(slot)) return slot;
		}

		return -1;
	}

	/// <summary>
	///     Whether the seat may hold a team. A seat on team zero is a spectator, which takes no player seat and so is always
	///     allowed; a seat that plays has to sit inside the player seats and has to find one free.
	/// </summary>
	private bool CanSeatPlayTeam(int slot, int team, int heldTeam)
	{
		if (team < 1) return true;
		if (heldTeam >= 1) return true;
		if (slot > _maximumPlayers) return false;

		return PlayingSeatCount < _maximumPlayers;
	}

	private string ResolveJoinTeamName()
	{
		int teamCount = Roster?.TeamCount ?? LobbyLimits.DefaultTeamCount;
		int[] members = new int[teamCount + 1];

		foreach (LobbySeat seat in _seats.Values)
		{
			if (seat.Team < 1 || seat.Team > teamCount) continue;
			members[seat.Team]++;
		}

		int chosenTeam = 1;
		for (int team = 1; team <= teamCount; team++)
		{
			if (members[team] == 0)
			{
				chosenTeam = team;
				break;
			}

			if (members[team] < members[chosenTeam]) chosenTeam = team;
		}

		return $"Team {chosenTeam}";
	}

	private uint ResolveJoinColorRgba(int slot)
	{
		for (int paletteIndex = PlayerColorPalette.NeutralPlayerIndex + 1; paletteIndex < PlayerColorPalette.Count; paletteIndex++)
		{
			uint candidate = PackPaletteColor(paletteIndex);
			if (!IsColorHeldByAnotherSeat(slot, candidate)) return candidate;
		}

		return PackPaletteColor(slot);
	}

	private bool IsColorHeldByAnotherSeat(int slot, uint colorRgba)
	{
		foreach (var pair in _seats)
		{
			if (pair.Key == slot) continue;
			if (pair.Value.ColorRgba == colorRgba) return true;
		}

		return false;
	}

	/// <summary>
	///     Leaves a name alone unless another seat already holds it, in which case this seat's number is appended. Two peers
	///     that never signed in, or that share a Steam persona name, would otherwise be described by the same string in the
	///     roster and in chat.
	/// </summary>
	private string MakeNameDistinct(string name, int slot)
	{
		if (string.IsNullOrEmpty(name)) return name;

		foreach (var pair in _seats)
		{
			if (pair.Key == slot) continue;
			if (!string.Equals(pair.Value.Name, name, StringComparison.OrdinalIgnoreCase)) continue;

			string suffix = $" {slot}";
			return LobbyLimits.Sanitize(name, LobbyLimits.MaximumPlayerNameLength - suffix.Length) + suffix;
		}

		return name;
	}

	private static string DefaultName(string? name, int slot, bool isHost)
	{
		string sanitized = LobbyLimits.SanitizeDisplayName(name);
		if (sanitized.Length > 0) return sanitized;
		return isHost ? "Host" : $"Player {slot}";
	}

	private static int ResolveTeamNumber(string? teamName, int slot)
	{
		if (string.Equals(teamName, LobbyLimits.SpectatorTeamName, StringComparison.Ordinal)) return 0;

		if (!string.IsNullOrWhiteSpace(teamName) && teamName.StartsWith("Team ", StringComparison.Ordinal)
			&& int.TryParse(teamName.AsSpan(5), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int team) && team > 0)
		{
			return team;
		}

		return slot;
	}

	private static uint PackPaletteColor(int slot)
	{
		PlayerColorEntry entry = PlayerColorPalette.Palette[Math.Clamp(slot, 0, PlayerColorPalette.Count - 1)];
		uint red = (uint)Math.Clamp(MathF.Round(entry.Red * 255f), 0f, 255f);
		uint green = (uint)Math.Clamp(MathF.Round(entry.Green * 255f), 0f, 255f);
		uint blue = (uint)Math.Clamp(MathF.Round(entry.Blue * 255f), 0f, 255f);
		return (red << 24) | (green << 16) | (blue << 8) | 255u;
	}

	private static string ResolveMapName(ServerArguments arguments)
	{
		if (!string.IsNullOrWhiteSpace(arguments.MapName)) return arguments.MapName;
		string fallback = Path.GetFileName(Path.TrimEndingDirectorySeparator(arguments.MapPath));
		return string.IsNullOrEmpty(fallback) ? "map" : fallback;
	}

	private string ResolveMapVersion(ServerArguments arguments)
	{
		try
		{
			MapManifest manifest = MapFileService.LoadManifest(arguments.MapPath);
			if (!string.IsNullOrWhiteSpace(manifest.Version)) return manifest.Version;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
		{
			_log.Write($"the lobby could not read its map's manifest: {exception.Message}");
		}

		return "1.0.0";
	}

	private int ResolveMaximumPlayers(ServerArguments arguments)
	{
		if (arguments.MaxPlayers > 0) return Math.Min(arguments.MaxPlayers, LobbyLimits.MaximumSeats);

		try
		{
			var metadata = MapFileService.LoadMetadata(arguments.MapPath);
			int slots = metadata.MapProperties?.GetPlayablePlayerSlots() ?? 0;
			if (slots > 0) return Math.Min(slots, LobbyLimits.MaximumSeats);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
		{
			_log.Write($"the lobby could not read its map's metadata: {exception.Message}");
		}

		return 8;
	}
}
