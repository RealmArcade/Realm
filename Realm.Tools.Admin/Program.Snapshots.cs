using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandLine;
using Realm.Shared;
using Realm.Shared.Accounts;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Tools.Admin;


public static partial class Program
{
	private static int ExecuteExportEvents(AdminExportEventsOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		DateTime? sinceUtc = null;
		if (!string.IsNullOrWhiteSpace(options.Since) && DateTime.TryParse(options.Since, out var parsed))
		{
			sinceUtc = parsed.ToUniversalTime();
		}

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Export Cluster Events");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:    {serverUrl}");
		Console.WriteLine($"Since:     {(sinceUtc.HasValue ? sinceUtc.Value.ToString("o") : "All History")}");
		Console.WriteLine($"Limit:     {options.Limit}");
		Console.WriteLine($"Output:    {options.OutputFile}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var events = client.GetClusterEventsAsync(sinceUtc, options.Limit).GetAwaiter().GetResult();

			string json = JsonSerializer.Serialize(events, new JsonSerializerOptions { WriteIndented = true });
			File.WriteAllText(options.OutputFile, json);

			Console.WriteLine($"[SUCCESS] Exported {events.Count} cluster events to '{options.OutputFile}'.");
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Failed to export events: {ex.Message}");
			return 1;
		}
	}

	private static int ExecutePruneCas(AdminPruneCasOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		using var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Server-Side CAS Prune");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:    {serverUrl}");
		Console.WriteLine($"Admin Key: {keyPair.PublicKeyBase64}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var result = client.PruneServerCasAsync(keyPair.ExportPrivateKeyBase64()).GetAwaiter().GetResult();

			if (result.Success)
			{
				Console.WriteLine($"[SUCCESS] {result.Message}");
				if (result.TotalScanned > 0 || result.OrphansPruned > 0 || result.BytesFreed > 0)
				{
					Console.WriteLine($"Total Scanned:   {result.TotalScanned}");
					Console.WriteLine($"Orphans Pruned:  {result.OrphansPruned}");
					Console.WriteLine($"Corrupt Pruned:  {result.CorruptPruned}");
					Console.WriteLine($"Bytes Freed:     {result.BytesFreed} bytes");
				}

				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] {result.Message}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] CAS prune failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteDigest(AdminDigestOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Database State Digest");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server: {serverUrl}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var digest = client.GetStateDigestAsync().GetAwaiter().GetResult();

			if (digest != null)
			{
				Console.WriteLine($"Root State Digest: {digest.StateDigest}");
				Console.WriteLine($"Server Timestamp:  {digest.ServerTimestampUtc:o}");
				Console.WriteLine();
				Console.WriteLine("Collection Hashes & Counts:");
				foreach (var col in digest.CollectionHashes.Keys.OrderBy(k => k))
				{
					int count = digest.CollectionCounts.TryGetValue(col, out var c) ? c : 0;
					Console.WriteLine($"  {col,-20} [{count,4} items] Hash: {digest.CollectionHashes[col]}");
				}

				Console.WriteLine("=================================================");
				return 0;
			}
			else
			{
				Console.Error.WriteLine("[FAILED] Could not retrieve state digest from server.");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteSnapshot(AdminSnapshotOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Export Database Snapshot");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server: {serverUrl}");
		Console.WriteLine($"Output: {options.OutputFile}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var snapshot = client.GetSnapshotAsync().GetAwaiter().GetResult();

			if (snapshot != null)
			{
				string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
				File.WriteAllText(options.OutputFile, json);

				Console.WriteLine($"[SUCCESS] Snapshot exported to '{options.OutputFile}'.");
				Console.WriteLine($"State Digest:   {snapshot.StateDigest}");
				Console.WriteLine($"Creators:       {snapshot.Creators.Count}");
				Console.WriteLine($"Published Maps: {snapshot.PublishedMaps.Count}");
				Console.WriteLine($"Map Stats:      {snapshot.MapStats.Count}");
				Console.WriteLine($"Generated At:   {snapshot.GeneratedAtUtc:o}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine("[FAILED] Could not retrieve snapshot from server.");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteRestoreSnapshot(AdminRestoreSnapshotOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		using var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		if (!File.Exists(options.InputFile))
		{
			Console.Error.WriteLine($"[ERROR] File '{options.InputFile}' not found.");
			return 1;
		}

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Restore Database Snapshot");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:    {serverUrl}");
		Console.WriteLine($"Input:     {options.InputFile}");
		Console.WriteLine($"Admin Key: {keyPair.PublicKeyBase64}");
		Console.WriteLine();

		try
		{
			string json = File.ReadAllText(options.InputFile);
			var snapshot = JsonSerializer.Deserialize<ClusterSnapshotDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
			if (snapshot == null)
			{
				Console.Error.WriteLine("[ERROR] Could not parse snapshot JSON.");
				return 1;
			}

			var client = new DistributionClient(serverUrl);
			var result = client.ApplySnapshotAsync(snapshot, keyPair.ExportPrivateKeyBase64()).GetAwaiter().GetResult();

			if (result.Success)
			{
				Console.WriteLine($"[SUCCESS] Snapshot applied: {result.Message}");
				Console.WriteLine($"Computed Digest: {result.ComputedStateDigest}");
				Console.WriteLine($"Expected Digest: {result.ExpectedStateDigest}");
				Console.WriteLine($"Creators:        {result.CreatorsImported}");
				Console.WriteLine($"Maps:            {result.MapsImported}");
				Console.WriteLine($"Stats:           {result.StatsImported}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] {result.Message}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Restore failed: {ex.Message}");
			return 1;
		}
	}
}
