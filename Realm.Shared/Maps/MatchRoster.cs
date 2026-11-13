using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Realm.Ecs;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Shared.Network;
using Realm.Shared.Simulation;

namespace Realm.Shared.Maps;

public readonly record struct MatchRosterSeat(int Slot, long AccountId, string Name, int Team, string TeamName, bool IsComputer, int Difficulty = (int)LobbyBotDifficulty.Hard, string Faction = "");

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
			TeamNameStringId = StringRegistry.GetOrAdd("Neutral"),
			Faction = string.Empty
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
				TeamNameStringId = StringRegistry.GetOrAdd($"Team {index}"),
				Faction = string.Empty
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
			players[seat.Slot].Faction = LobbyLimits.IsRandomFaction(seat.Faction) ? string.Empty : seat.Faction ?? string.Empty;
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

	private const string Header = "realm-match-roster 5";
	private const string OptionsSection = "options";
	private const char Separator = '\t';
	private const int ColumnCount = 8;

	public static void Write(string path, IReadOnlyList<MatchRosterSeat> seats, MatchOptionSet? matchOptions = null)
	{
		var builder = new StringBuilder();
		builder.Append(Header).Append('\n');

		builder.Append(OptionsSection);
		if (matchOptions != null)
		{
			MatchOptionValue[] values = matchOptions.ToValues();
			Array.Sort(values, static (left, right) => left.Option.CompareTo(right.Option));

			for (int index = 0; index < values.Length; index++)
			{
				builder.Append(Separator).Append((int)values[index].Option).Append('=').Append(values[index].Value);
			}
		}

		builder.Append('\n');

		for (int index = 0; index < seats.Count; index++)
		{
			MatchRosterSeat seat = seats[index];
			builder.Append(seat.Slot.ToString(CultureInfo.InvariantCulture)).Append(Separator);
			builder.Append(seat.AccountId.ToString(CultureInfo.InvariantCulture)).Append(Separator);
			builder.Append(Sanitize(seat.Name)).Append(Separator);
			builder.Append(seat.Team.ToString(CultureInfo.InvariantCulture)).Append(Separator);
			builder.Append(Sanitize(seat.TeamName)).Append(Separator);
			builder.Append(seat.IsComputer ? '1' : '0').Append(Separator);
			builder.Append(seat.Difficulty.ToString(CultureInfo.InvariantCulture)).Append(Separator);
			builder.Append(Sanitize(seat.Faction)).Append('\n');
		}

		string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		File.WriteAllText(path, builder.ToString());
	}

	public static bool TryRead(string path, out MatchRosterSeat[] seats, out MatchOptionSet matchOptions, out string error)
	{
		seats = Array.Empty<MatchRosterSeat>();
		matchOptions = new MatchOptionSet();
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

		string[] optionColumns = lines.Length < 2 ? Array.Empty<string>() : lines[1].Split(Separator);
		if (optionColumns.Length == 0 || !string.Equals(optionColumns[0], OptionsSection, StringComparison.Ordinal))
		{
			error = $"the match roster '{path}' does not state the match options on its second line.";
			return false;
		}

		for (int column = 1; column < optionColumns.Length; column++)
		{
			string entry = optionColumns[column];
			int separator = entry.IndexOf('=');
			if (separator <= 0 || !int.TryParse(entry[..separator], NumberStyles.Integer, CultureInfo.InvariantCulture, out int option)
				|| !Enum.IsDefined(typeof(MatchOption), option)
				|| !int.TryParse(entry[(separator + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
			{
				error = $"the match roster '{path}' states the option '{entry}', which is not one an option and a value.";
				return false;
			}

			if (!matchOptions.Set((MatchOption)option, value))
			{
				error = $"the match roster '{path}' states the option '{entry}', which the match could not play.";
				return false;
			}
		}

		var parsed = new List<MatchRosterSeat>(lines.Length - 2);
		for (int index = 2; index < lines.Length; index++)
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

			if (!int.TryParse(columns[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int difficulty)
				|| difficulty < (int)LobbyBotDifficulty.Easy
				|| difficulty > (int)LobbyBotDifficulty.Hard)
			{
				error = $"the match roster line {index + 1} of '{path}' does not name a bot difficulty.";
				return false;
			}

			parsed.Add(new MatchRosterSeat(slot, accountId, columns[2], team, columns[4], columns[5][0] == '1', difficulty, columns[7]));
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
