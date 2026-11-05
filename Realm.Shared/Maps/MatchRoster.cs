using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;

namespace Realm.Shared.Maps;

public readonly record struct MatchRosterSeat(int Slot, long AccountId, string Name, int Team, string TeamName, bool IsComputer);

public static class MatchRoster
{
	public const int NeutralSlot = 0;

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

	public static PlayerEntity ResolvePlayer(Entity[] playerEntities, int playerIndex)
	{
		if (playerEntities == null || playerEntities.Length == 0) return new PlayerEntity(Entity.Null);

		int clamped = playerIndex < 0 || playerIndex >= playerEntities.Length ? NeutralSlot : playerIndex;
		return new PlayerEntity(playerEntities[clamped]);
	}
}

public static class MatchRosterFile
{
	public const string FileSuffix = ".roster.txt";

	private const string Header = "realm-match-roster 2";
	private const char Separator = '\t';
	private const int ColumnCount = 6;

	public static void Write(string path, IReadOnlyList<MatchRosterSeat> seats)
	{
		var builder = new StringBuilder();
		builder.Append(Header).Append('\n');

		for (int index = 0; index < seats.Count; index++)
		{
			MatchRosterSeat seat = seats[index];
			builder.Append(seat.Slot.ToString(CultureInfo.InvariantCulture)).Append(Separator);
			builder.Append(seat.AccountId.ToString(CultureInfo.InvariantCulture)).Append(Separator);
			builder.Append(Sanitize(seat.Name)).Append(Separator);
			builder.Append(seat.Team.ToString(CultureInfo.InvariantCulture)).Append(Separator);
			builder.Append(Sanitize(seat.TeamName)).Append(Separator);
			builder.Append(seat.IsComputer ? '1' : '0').Append('\n');
		}

		string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		File.WriteAllText(path, builder.ToString());
	}

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
				|| !long.TryParse(columns[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long accountId)
				|| !int.TryParse(columns[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int team))
			{
				error = $"the match roster line {index + 1} of '{path}' does not name a slot and a team.";
				return false;
			}

			if (columns[5].Length != 1 || (columns[5][0] != '0' && columns[5][0] != '1'))
			{
				error = $"the match roster line {index + 1} of '{path}' does not say whether the seat is a computer.";
				return false;
			}

			parsed.Add(new MatchRosterSeat(slot, accountId, columns[2], team, columns[4], columns[5][0] == '1'));
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
