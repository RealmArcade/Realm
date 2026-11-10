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
	private static int ExecuteRemoveManifest(AdminRemoveManifestOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		using var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string mapTitle = options.Map.Trim();
		string? mapVersion = string.IsNullOrWhiteSpace(options.Version) ? null : options.Version.Trim();
		bool isAllVersions = mapVersion == null;

		PrintRemoveManifestHeader(serverUrl, mapTitle, mapVersion, isAllVersions, keyPair.PublicKeyBase64);

		try
		{
			return ProcessRemoveManifest(options, serverUrl, keyPair, mapTitle, mapVersion, isAllVersions);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Remove manifest failed: {ex.Message}");
			return 1;
		}
	}

	private static void PrintRemoveManifestHeader(string serverUrl, string mapTitle, string? mapVersion, bool isAllVersions, string publicKeyBase64)
	{
		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Remove Published Map Manifest");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:     {serverUrl}");
		Console.WriteLine($"Map Title:  {mapTitle}");
		Console.WriteLine($"Version:    {(isAllVersions ? "[ALL VERSIONS]" : mapVersion)}");
		Console.WriteLine($"Admin Key:  {publicKeyBase64}");
		Console.WriteLine();
	}

	private static int ProcessRemoveManifest(AdminRemoveManifestOptions options, string serverUrl, AdminKeySession keyPair, string mapTitle, string? mapVersion, bool isAllVersions)
	{
		var client = new DistributionClient(serverUrl);
		var result = isAllVersions
			? client.RemoveAllManifestVersionsAsync(mapTitle, keyPair.ExportPrivateKeyBase64()).GetAwaiter().GetResult()
			: client.RemoveManifestVersionAsync(mapTitle, mapVersion!, keyPair.ExportPrivateKeyBase64()).GetAwaiter().GetResult();

		if (!result.Success)
		{
			Console.Error.WriteLine($"[FAILED] {result.Message}");
			return 1;
		}

		PrintRemoveManifestSuccess(result);

		if (options.Prune)
		{
			ProcessSubsequentPrune(client, keyPair);
		}

		Console.WriteLine("=================================================");
		return 0;
	}

	private static void PrintRemoveManifestSuccess(RemoveManifestResponseDto result)
	{
		Console.WriteLine($"[SUCCESS] {result.Message}");
		Console.WriteLine($"Manifests Deleted:   {result.ManifestsDeleted}");
		Console.WriteLine($"DB Records Removed:  {result.DbRecordsRemoved}");
		if (result.DeletedManifestFiles.Count > 0)
		{
			Console.WriteLine($"Deleted Files:       {string.Join(", ", result.DeletedManifestFiles)}");
		}
	}

	private static void ProcessSubsequentPrune(DistributionClient client, AdminKeySession keyPair)
	{
		Console.WriteLine();
		Console.WriteLine("Running subsequent CAS prune...");
		var pruneResult = client.PruneServerCasAsync(keyPair.ExportPrivateKeyBase64()).GetAwaiter().GetResult();

		if (!pruneResult.Success)
		{
			Console.Error.WriteLine($"[WARNING] Subsequent CAS prune failed: {pruneResult.Message}");
			return;
		}

		Console.WriteLine($"[SUCCESS] {pruneResult.Message}");
		if (pruneResult.TotalScanned > 0 || pruneResult.OrphansPruned > 0 || pruneResult.BytesFreed > 0)
		{
			Console.WriteLine($"Total Scanned:   {pruneResult.TotalScanned}");
			Console.WriteLine($"Orphans Pruned:  {pruneResult.OrphansPruned}");
			Console.WriteLine($"Corrupt Pruned:  {pruneResult.CorruptPruned}");
			Console.WriteLine($"Bytes Freed:     {pruneResult.BytesFreed} bytes");
		}
	}
}
