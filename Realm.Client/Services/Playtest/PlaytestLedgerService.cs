using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;
using Realm.Shared.Steam;

namespace Realm.Client.Services.Playtest;

/// <summary>What a map's ledger says about the people who have played it.</summary>
/// <param name="Testers">How many distinct Steam identities have answered for this map.</param>
/// <param name="NotReady">How many of them asked for more work. Only the author is ever shown this.</param>
public readonly record struct PlaytestLedgerSummary(int Testers, int NotReady);

/// <summary>A promoted item as the greenlight record keeps it.</summary>
/// <param name="OwnerSteamId">The account that owned the item when it was promoted, which is what a change of owner resets.</param>
/// <param name="MapKey">The key of the map that reached its threshold.</param>
/// <param name="GreenlitAt">When it happened, for the author's own eyes.</param>
public sealed record PlaytestGreenlightRecord(ulong OwnerSteamId, string MapKey, DateTimeOffset GreenlitAt);

/// <summary>
///     The playtest ledger and the greenlight record, both kept on the machine that reads them.
///     <para>
///         The ledger is one file per map under the user's local application data, never inside a map folder, so it cannot
///         be uploaded, exported or shipped and a tester's SteamID never travels with the map. Each record is stored as the
///         opaque code the tester was given, so the file is not a browsable list of who played what.
///     </para>
///     <para>
///         Anyone who wants to can edit these files, and that is accepted: the envelope's key ships in every build, so a
///         determined author can mint a vote for any SteamID, and anyone who uses SteamCMD skips the whole path. What this
///         defends is a player who did not realise their map was unfinished, not a cheater.
///     </para>
/// </summary>
public sealed class PlaytestLedgerService
{
	private const string GreenlightFileName = "greenlit.json";

	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	/// <summary>
	///     Adds one vote to a map's ledger. A code from an identity the ledger already holds is ignored, which is what makes
	///     pasting the same code twice append nothing; a vote the authority broadcast for a seat that changed its mind
	///     replaces that seat's earlier answer.
	/// </summary>
	/// <param name="mapKey">The map the vote counts for.</param>
	/// <param name="code">The code that carries the vote.</param>
	/// <param name="replaceExistingAnswer">Whether this vote replaces an earlier answer from the same identity.</param>
	/// <param name="failure">Why the code was refused, empty on success.</param>
	/// <returns>Whether the ledger now holds a vote it did not hold before.</returns>
	public bool TryAppendCode(string mapKey, string? code, bool replaceExistingAnswer, out string failure)
	{
		if (!PlaytestVoteCode.TryReadFor(code, mapKey, out PlaytestVoteRecord record, out failure)) return false;

		string path = GetLedgerPath(mapKey);
		List<string> codes = ReadCodes(path);

		int existingIndex = -1;
		for (int index = 0; index < codes.Count; index++)
		{
			if (!PlaytestVoteCode.TryRead(codes[index], out PlaytestVoteRecord existing, out _)) continue;
			if (existing.SteamId != record.SteamId) continue;

			existingIndex = index;
			break;
		}

		if (existingIndex >= 0)
		{
			if (!replaceExistingAnswer)
			{
				failure = string.Empty;
				return false;
			}

			codes[existingIndex] = code!.Trim();
		}
		else
		{
			codes.Add(code!.Trim());
		}

		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllLines(path, codes, Encoding.UTF8);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			failure = $"The playtest ledger could not be written: {exception.Message}";
			return false;
		}

		return true;
	}

	/// <summary>
	///     Reads what a map's ledger says: how many distinct Steam identities have answered and how many of them asked for
	///     more work.
	/// </summary>
	/// <param name="mapKey">The map to read.</param>
	/// <returns>The ledger's summary, empty when the map has no ledger.</returns>
	public PlaytestLedgerSummary Read(string mapKey)
	{
		string path = GetLedgerPath(mapKey);
		List<string> codes = ReadCodes(path);

		var testers = new HashSet<ulong>();
		int notReady = 0;
		var countedNotReady = new HashSet<ulong>();

		foreach (string code in codes)
		{
			if (!PlaytestVoteCode.TryRead(code, out PlaytestVoteRecord record, out _)) continue;
			if (!testers.Add(record.SteamId)) continue;
			if (record.Vote != PlaytestVote.NotReady) continue;
			if (countedNotReady.Add(record.SteamId)) notReady++;
		}

		return new PlaytestLedgerSummary(testers.Count, notReady);
	}

	/// <summary>
	///     Whether an item may be published publicly: the map reached its threshold, or the author has none to reach.
	/// </summary>
	/// <param name="mapKey">The map being published.</param>
	/// <param name="itemId">The Workshop item the map publishes to, or zero before the first publish.</param>
	/// <param name="ownerSteamId">The account publishing now.</param>
	/// <param name="threshold">How many distinct testers this map asks for; zero means none.</param>
	/// <returns>Whether a public publish is allowed.</returns>
	public bool IsGreenlit(string mapKey, ulong itemId, ulong ownerSteamId, int threshold)
	{
		if (threshold <= 0) return true;
		if (Read(itemId, ownerSteamId, mapKey) != null) return true;

		return Read(mapKey).Testers >= threshold;
	}

	/// <summary>
	///     Records that an item has been published publicly, so later publishes need no new votes and a crash fix does not
	///     cost another 25 testers. The record is kept by Workshop item id, so it follows the item rather than the folder.
	/// </summary>
	/// <param name="itemId">The item that went public.</param>
	/// <param name="ownerSteamId">The account that owns it.</param>
	/// <param name="mapKey">The map it carries.</param>
	public void MarkGreenlit(ulong itemId, ulong ownerSteamId, string mapKey)
	{
		if (itemId == 0) return;

		Dictionary<string, PlaytestGreenlightRecord> records = ReadGreenlightRecords();
		records[itemId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new PlaytestGreenlightRecord(ownerSteamId, mapKey, DateTimeOffset.UtcNow);
		WriteGreenlightRecords(records);
	}

	/// <summary>
	///     Whether this item was already promoted, and still belongs to the account that promoted it. A change of owner is
	///     the one thing that resets a greenlight, because the new owner did not earn it.
	/// </summary>
	/// <param name="itemId">The item to ask about.</param>
	/// <param name="ownerSteamId">The account that owns it now.</param>
	/// <param name="mapKey">The map it carries.</param>
	/// <returns>The record when the item is still greenlit, otherwise null.</returns>
	public PlaytestGreenlightRecord? Read(ulong itemId, ulong ownerSteamId, string mapKey)
	{
		if (itemId == 0) return null;

		Dictionary<string, PlaytestGreenlightRecord> records = ReadGreenlightRecords();
		if (!records.TryGetValue(itemId.ToString(System.Globalization.CultureInfo.InvariantCulture), out PlaytestGreenlightRecord? record) || record == null) return null;

		if (record.OwnerSteamId != ownerSteamId) return null;
		if (!string.Equals(record.MapKey, mapKey, StringComparison.Ordinal)) return null;
		return record;
	}

	/// <summary>The file one map's ledger is kept in.</summary>
	/// <param name="mapKey">The map's key.</param>
	/// <returns>The ledger's path.</returns>
	public static string GetLedgerPath(string mapKey) => Path.Combine(PlaytestStore.GetRootDirectory(), mapKey + ".bin");

	private static string GetGreenlightPath() => Path.Combine(PlaytestStore.GetRootDirectory(), GreenlightFileName);

	private static List<string> ReadCodes(string path)
	{
		try
		{
			if (!File.Exists(path)) return new List<string>();
			return new List<string>(File.ReadAllLines(path, Encoding.UTF8));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			GameLog.LogError($"[Playtest] The ledger at '{path}' could not be read: {exception.Message}");
			return new List<string>();
		}
	}

	private static Dictionary<string, PlaytestGreenlightRecord> ReadGreenlightRecords()
	{
		try
		{
			string path = GetGreenlightPath();
			if (!File.Exists(path)) return new Dictionary<string, PlaytestGreenlightRecord>(StringComparer.Ordinal);

			string json = File.ReadAllText(path, Encoding.UTF8);
			return JsonSerializer.Deserialize<Dictionary<string, PlaytestGreenlightRecord>>(json, JsonOptions)
				?? new Dictionary<string, PlaytestGreenlightRecord>(StringComparer.Ordinal);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			GameLog.LogError($"[Playtest] The greenlight record could not be read: {exception.Message}");
			return new Dictionary<string, PlaytestGreenlightRecord>(StringComparer.Ordinal);
		}
	}

	private static void WriteGreenlightRecords(Dictionary<string, PlaytestGreenlightRecord> records)
	{
		try
		{
			string path = GetGreenlightPath();
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, JsonSerializer.Serialize(records, JsonOptions), Encoding.UTF8);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			GameLog.LogError($"[Playtest] The greenlight record could not be written: {exception.Message}");
		}
	}
}
