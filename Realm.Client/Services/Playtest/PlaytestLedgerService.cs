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
public readonly record struct PlaytestLedgerSummary(int Testers, int NotReady);	/// <summary>A promoted map as the greenlight record keeps it.</summary>
	/// <param name="OwnerSteamId">The account that owned the map when it was promoted, which is what a change of author resets.</param>
	/// <param name="MapKey">The key of the map that reached its threshold.</param>
	/// <param name="ItemId">The Workshop item it was promoted through, or zero when it was promoted outside the Workshop.</param>
	/// <param name="GreenlitAt">When it happened, for the author's own eyes.</param>
	public sealed record PlaytestGreenlightRecord(ulong OwnerSteamId, string MapKey, ulong ItemId, DateTimeOffset GreenlitAt);

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
	/// <param name="ledgerKey">The key the map's ledger and greenlight record are filed under: its name and its author.</param>
	/// <param name="codeMapKey">The key the code itself names, which is the map's name alone.</param>
	/// <param name="code">The code that carries the vote.</param>
	/// <param name="replaceExistingAnswer">Whether this vote replaces an earlier answer from the same identity.</param>
	/// <param name="failure">Why the code was refused, empty on success.</param>
	/// <returns>Whether the ledger now holds a vote it did not hold before.</returns>
	public bool TryAppendCode(string ledgerKey, string codeMapKey, string? code, bool replaceExistingAnswer, out string failure)
	{
		if (!PlaytestVoteCode.TryReadFor(code, codeMapKey, out PlaytestVoteRecord record, out failure)) return false;

		string path = GetLedgerPath(ledgerKey);
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
	/// <param name="mapKey">The key the map's ledger is filed under: its name and its author.</param>
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
	///     Whether a map may be published publicly: it reached its threshold, or the author has none to reach.
	/// </summary>
	/// <param name="mapKey">The key the map is filed under: its name and its author.</param>
	/// <param name="ownerSteamId">The account publishing now.</param>
	/// <param name="threshold">How many distinct testers this map asks for; zero means none.</param>
	/// <returns>Whether a public publish is allowed.</returns>
	public bool IsGreenlit(string mapKey, ulong ownerSteamId, int threshold)
	{
		if (threshold <= 0) return true;
		if (ReadGreenlightRecord(mapKey, ownerSteamId) != null) return true;

		return Read(mapKey).Testers >= threshold;
	}

	/// <summary>
	///     Records that a map has been published publicly, so later publishes need no new votes and a crash fix does not cost
	///     another 25 testers. The record is kept by map name and author, not by Workshop item, so it survives a new item, a
	///     move between AppIDs and every new version of the same map.
	/// </summary>
	/// <param name="mapKey">The key the map is filed under: its name and its author.</param>
	/// <param name="ownerSteamId">The account that owns it.</param>
	/// <param name="itemId">The item it was published through, or zero when it was promoted outside the Workshop.</param>
	public void MarkGreenlit(string mapKey, ulong ownerSteamId, ulong itemId)
	{
		if (string.IsNullOrEmpty(mapKey)) return;

		Dictionary<string, PlaytestGreenlightRecord> records = ReadGreenlightRecords();
		records[mapKey] = new PlaytestGreenlightRecord(ownerSteamId, mapKey, itemId, DateTimeOffset.UtcNow);
		WriteGreenlightRecords(records);
	}

	/// <summary>
	///     Whether this map was already promoted, and still belongs to the account that promoted it. A change of author is
	///     the one thing that resets a greenlight, because the new author did not earn it.
	/// </summary>
	/// <param name="mapKey">The key the map is filed under: its name and its author.</param>
	/// <param name="ownerSteamId">The account that owns it now.</param>
	/// <returns>The record when the map is still greenlit, otherwise null.</returns>
	public PlaytestGreenlightRecord? ReadGreenlightRecord(string mapKey, ulong ownerSteamId)
	{
		if (string.IsNullOrEmpty(mapKey)) return null;

		Dictionary<string, PlaytestGreenlightRecord> records = ReadGreenlightRecords();
		if (!records.TryGetValue(mapKey, out PlaytestGreenlightRecord? record) || record == null) return null;

		if (record.OwnerSteamId != ownerSteamId) return null;
		return record;
	}

	/// <summary>The file one author's ledger for one map is kept in.</summary>
	/// <param name="mapKey">The map's key: its name and its author.</param>
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
