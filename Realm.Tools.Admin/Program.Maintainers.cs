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
	private static string? ResolveTargetPublicKey(string? targetKeyPath, string? targetPubKey)
	{
		if (!string.IsNullOrWhiteSpace(targetPubKey))
		{
			return targetPubKey.Trim();
		}

		if (!string.IsNullOrWhiteSpace(targetKeyPath))
		{
			return ParsePublicKeyFromFile(targetKeyPath);
		}

		Console.Error.WriteLine("Error: Please specify either --target-key <path to .rkey> or -p/--pubkey <base64 public key>.");
		return null;
	}

	private static int ExecuteAddMaintainer(AdminAddMaintainerOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		using var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string mapTitle = options.Map.Trim();
		string? targetPubKey = ResolveTargetPublicKey(options.TargetKey, options.PubKey);
		if (string.IsNullOrWhiteSpace(targetPubKey)) return 1;

		string payload = $"add_maintainer:{mapTitle.ToLowerInvariant()}:{targetPubKey.Trim()}";
		string signature = keyPair.CreateSignature(payload);

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Add Map Maintainer");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:          {serverUrl}");
		Console.WriteLine($"Map:             {mapTitle}");
		Console.WriteLine($"Maintainer Key:  {targetPubKey}");
		Console.WriteLine($"Requester Key:   {keyPair.PublicKeyBase64}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var request = new AddMapMaintainerRequest
			{
				MapTitle = mapTitle,
				MaintainerPublicKey = targetPubKey,
				RequesterPublicKey = keyPair.PublicKeyBase64,
				Signature = signature
			};

			var response = client.AddMapMaintainerAsync(request).GetAwaiter().GetResult();
			return HandleMapMaintainersResponse(response, true);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Add maintainer failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteRemoveMaintainer(AdminRemoveMaintainerOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		using var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string mapTitle = options.Map.Trim();
		string? targetPubKey = ResolveTargetPublicKey(options.TargetKey, options.PubKey);
		if (string.IsNullOrWhiteSpace(targetPubKey)) return 1;

		string payload = $"remove_maintainer:{mapTitle.ToLowerInvariant()}:{targetPubKey.Trim()}";
		string signature = keyPair.CreateSignature(payload);

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Remove Map Maintainer");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:          {serverUrl}");
		Console.WriteLine($"Map:             {mapTitle}");
		Console.WriteLine($"Maintainer Key:  {targetPubKey}");
		Console.WriteLine($"Requester Key:   {keyPair.PublicKeyBase64}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var request = new RemoveMapMaintainerRequest
			{
				MapTitle = mapTitle,
				MaintainerPublicKey = targetPubKey,
				RequesterPublicKey = keyPair.PublicKeyBase64,
				Signature = signature
			};

			var response = client.RemoveMapMaintainerAsync(request).GetAwaiter().GetResult();
			return HandleMapMaintainersResponse(response, true);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Remove maintainer failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteListMaintainers(AdminListMaintainersOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		string mapTitle = options.Map.Trim();

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - List Map Maintainers");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:  {serverUrl}");
		Console.WriteLine($"Map:     {mapTitle}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var response = client.GetMapMaintainersAsync(mapTitle).GetAwaiter().GetResult();
			return HandleMapMaintainersResponse(response, false);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] List maintainers failed: {ex.Message}");
			return 1;
		}
	}

	private static int HandleMapMaintainersResponse(MapMaintainersResponseDto response, bool includeSuccessMessage)
	{
		if (response.Success)
		{
			if (includeSuccessMessage)
			{
				Console.WriteLine($"[SUCCESS] {response.Message}");
			}
			Console.WriteLine($"Owner:              {(!string.IsNullOrEmpty(response.OwnerPublicKey) ? response.OwnerPublicKey : "(None)")}");
			Console.WriteLine($"Total Maintainers:  {response.Maintainers.Count}");
			for (int i = 0; i < response.Maintainers.Count; i++)
			{
				Console.WriteLine($"  [{i + 1}] {response.Maintainers[i]}");
			}

			Console.WriteLine("=================================================");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"[FAILED] {response.Message}");
			return 1;
		}
	}
}
