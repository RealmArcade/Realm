using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;

namespace Realm.Shared.Maps;

/// <summary>
///     One player a match is played by, as the lobby that arranged it knows them.
/// </summary>
/// <param name="Slot">The player slot the seat plays, which is the index the map's script addresses it by.</param>
/// <param name="Name">The name the seat is shown under.</param>
/// <param name="Team">The team the seat is on, which decides who its units treat as allies.</param>
/// <param name="TeamName">The team as players read it.</param>
/// <param name="IsComputer">Whether the match plays this seat itself instead of a person.</param>
public readonly record struct MatchRosterSeat(int Slot, string Name, int Team, string TeamName, bool IsComputer);

/// <summary>
///     The players a match starts with before a lobby hands out slots. Both processes build the same roster, so a
///     player slot means the same thing to whoever is simulating and whoever is rendering.
/// </summary>
public static class MatchRoster
{
	public const int NeutralSlot = 0;

	/// <summary>
	///     Slot zero is the neutral player the map's own scenery belongs to. The remaining slots are the match
	///     participants. Nobody starts with anything: what a player begins with is a rule the map authors, so the
	///     roster hands out zero and the map's own script grants whatever its rules call for.
	/// </summary>
	public static ScriptPlayer[] CreateDefaultPlayers()
	{
		var players = new ScriptPlayer[EcsWorld.MAX_PLAYERS];

		players[NeutralSlot] = new ScriptPlayer
		{
			Gold = 0f,
			Wood = 0f,
			Stone = 0f,
			Active = true,
			IsComputer = true,
			Team = 0,
			Name = "Neutral",
			KillCount = 0,
			TeamNameStringId = StringRegistry.GetOrAdd("Neutral")
		};

		for (int index = 1; index < EcsWorld.MAX_PLAYERS; index++)
		{
			players[index] = new ScriptPlayer
			{
				Gold = 0f,
				Wood = 0f,
				Stone = 0f,
				Active = index == 1,
				IsComputer = false,
				Team = index,
				Name = $"Player {index}",
				KillCount = 0,
				TeamNameStringId = StringRegistry.GetOrAdd($"Team {index}")
			};
		}

		return players;
	}

	/// <summary>
	///     Seats the players a lobby arranged before the map's script runs. A map asks who is playing and spawns their
	///     starting units while it initializes, so every seat - including the ones the match plays itself and the ones
	///     whose players have not connected yet - has to be in the roster by then. A seat the roster does not name is
	///     left inactive, which is what a slot nobody plays looks like.
	/// </summary>
	/// <param name="players">The default roster, which this rewrites in place.</param>
	/// <param name="seats">The players the lobby arranged, or nothing to leave the defaults alone.</param>
	public static void SeatPlayers(ScriptPlayer[] players, IReadOnlyList<MatchRosterSeat>? seats)
	{
		if (seats == null || seats.Count == 0) return;

		for (int index = 0; index < players.Length; index++)
		{
			if (index == NeutralSlot) continue;

			players[index].Active = false;
			players[index].IsComputer = false;
		}

		for (int index = 0; index < seats.Count; index++)
		{
			MatchRosterSeat seat = seats[index];
			if (seat.Slot <= NeutralSlot || seat.Slot >= players.Length) continue;

			string name = string.IsNullOrWhiteSpace(seat.Name) ? $"Player {seat.Slot}" : seat.Name;
			string teamName = string.IsNullOrWhiteSpace(seat.TeamName) ? $"Team {seat.Team}" : seat.TeamName;

			players[seat.Slot].Name = name;
			players[seat.Slot].Active = true;
			players[seat.Slot].IsComputer = seat.IsComputer;
			players[seat.Slot].Team = seat.Team;
			players[seat.Slot].TeamNameStringId = StringRegistry.GetOrAdd(teamName);
		}
	}

	/// <summary>The player entity a slot belongs to, falling back to the neutral slot for a slot a map did not use.</summary>
	public static PlayerEntity ResolvePlayer(Entity[] playerEntities, int playerIndex)
	{
		if (playerEntities == null || playerEntities.Length == 0) return new PlayerEntity(Entity.Null);

		int clamped = playerIndex < 0 || playerIndex >= playerEntities.Length ? NeutralSlot : playerIndex;
		return new PlayerEntity(playerEntities[clamped]);
	}
}

/// <summary>
///     The file a client hands its authority so a match is seated before its map script runs. The roster travels as a
///     file rather than as a flag because a match's players are a list, and because the authority loads the world and
///     starts the script before it is listening: by the time a peer could say who it is, the seats already exist.
/// </summary>
public static class MatchRosterFile
{
	/// <summary>The name a roster file is written under beside the match's log.</summary>
	public const string FileSuffix = ".roster.txt";

	private const string Header = "realm-match-roster 1";
	private const char Separator = '\t';
	private const int ColumnCount = 5;

	/// <summary>Writes the players a match is played by.</summary>
	/// <param name="path">Where the roster belongs.</param>
	/// <param name="seats">The seats the lobby arranged, in the order it listed them.</param>
	public static void Write(string path, IReadOnlyList<MatchRosterSeat> seats)
	{
		var builder = new StringBuilder();
		builder.Append(Header).Append('\n');

		for (int index = 0; index < seats.Count; index++)
		{
			MatchRosterSeat seat = seats[index];
			builder.Append(seat.Slot.ToString(CultureInfo.InvariantCulture)).Append(Separator);
			builder.Append(Sanitize(seat.Name)).Append(Separator);
			builder.Append(seat.Team.ToString(CultureInfo.InvariantCulture)).Append(Separator);
			builder.Append(Sanitize(seat.TeamName)).Append(Separator);
			builder.Append(seat.IsComputer ? '1' : '0').Append('\n');
		}

		string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		File.WriteAllText(path, builder.ToString());
	}

	/// <summary>
	///     Reads the players a match is played by. A roster that cannot be read is reported rather than half-seated:
	///     the authority would otherwise run a match whose map script spawned starting units for players who are not in
	///     it.
	/// </summary>
	public static bool TryRead(string path, out MatchRosterSeat[] seats, out string error)
	{
		seats = Array.Empty<MatchRosterSeat>();
		error = string.Empty;

		string[] lines;
		try
		{
			lines = File.ReadAllLines(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			error = $"the match roster '{path}' could not be read: {exception.Message}";
			return false;
		}

		if (lines.Length == 0 || !string.Equals(lines[0], Header, StringComparison.Ordinal))
		{
			error = $"the match roster '{path}' does not start with '{Header}'.";
			return false;
		}

		var parsed = new List<MatchRosterSeat>(lines.Length - 1);
		for (int index = 1; index < lines.Length; index++)
		{
			string line = lines[index];
			if (line.Length == 0) continue;

			string[] columns = line.Split(Separator);
			if (columns.Length != ColumnCount)
			{
				error = $"the match roster line {index + 1} of '{path}' has {columns.Length} columns, not {ColumnCount}.";
				return false;
			}

			if (!int.TryParse(columns[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot)
				|| !int.TryParse(columns[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int team))
			{
				error = $"the match roster line {index + 1} of '{path}' does not name a slot and a team.";
				return false;
			}

			if (columns[4].Length != 1 || (columns[4][0] != '0' && columns[4][0] != '1'))
			{
				error = $"the match roster line {index + 1} of '{path}' does not say whether the seat is a computer.";
				return false;
			}

			parsed.Add(new MatchRosterSeat(slot, columns[1], team, columns[3], columns[4][0] == '1'));
		}

		seats = parsed.ToArray();
		return true;
	}

	private static string Sanitize(string? text)
	{
		if (string.IsNullOrEmpty(text)) return string.Empty;

		var builder = new StringBuilder(text.Length);
		for (int index = 0; index < text.Length; index++)
		{
			char character = text[index];
			builder.Append(character is '\t' or '\n' or '\r' ? ' ' : character);
		}

		return builder.ToString();
	}
}
