using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandLine;
using NSec.Cryptography;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Tools.Admin;

[Verb("greenlight", HelpText = "Greenlight override for a map to be published without required metrics")]
public class AdminGreenlightOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey key file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("status", HelpText = "Check the greenlight status and community metrics of a map")]
public class AdminStatusOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option('v', "version", Required = false, Default = null, HelpText = "Map version to inspect")]
	public string? Version { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("unlock-name", HelpText = "Override the public key tied to a creator's username")]
public class AdminUnlockNameOptions
{
	[Option('u', "username", Required = true, HelpText = "Creator's Username")]
	public string Username { get; set; } = string.Empty;

	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey key file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option("target-key", Required = true, HelpText = "Path to the target user's .rkey key file.")]
	public string TargetKey { get; set; } = string.Empty;

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("info", HelpText = "Display server information")]
public class AdminInfoOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("export-events", HelpText = "Export cluster events from the node to a local JSON file.")]
public class AdminExportEventsOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }

	[Option('o', "out", Required = false, Default = "cluster_events.json", HelpText = "Output JSON file path.")]
	public string OutputFile { get; set; } = "cluster_events.json";

	[Option("since", Required = false, HelpText = "Optional ISO timestamp (e.g. 2026-01-01T00:00:00Z) to fetch events since.")]
	public string? Since { get; set; }

	[Option('l', "limit", Required = false, Default = 500, HelpText = "Maximum number of events to export.")]
	public int Limit { get; set; } = 500;
}

[Verb("prune-cas", HelpText = "Trigger server-side CAS integrity verification and orphan asset pruning.")]
public class AdminPruneCasOptions
{
	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey key file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("digest", HelpText = "Query and verify BLAKE3 database state digest across one or more nodes.")]
public class AdminDigestOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("snapshot", HelpText = "Download a complete database state snapshot from a node to a local JSON file.")]
public class AdminSnapshotOptions
{
	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }

	[Option('o', "out", Required = false, Default = "cluster_snapshot.json", HelpText = "Output JSON snapshot file path.")]
	public string OutputFile { get; set; } = "cluster_snapshot.json";
}

[Verb("restore-snapshot", HelpText = "Restore a database state snapshot file to a target node")]
public class AdminRestoreSnapshotOptions
{
	[Option('i', "in", Required = true, HelpText = "Input JSON snapshot file path.")]
	public string InputFile { get; set; } = string.Empty;

	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey key file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("remove-manifest", HelpText = "Remove a published map manifest.json from the registry server")]
public class AdminRemoveManifestOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option('v', "version", Required = false, Default = null, HelpText = "Map version to remove. If omitted, all versions of the map are removed.")]
	public string? Version { get; set; }

	[Option('k', "key", Required = false, HelpText = "Path to admin .rkey key file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }

	[Option('p', "prune", Required = false, Default = true, HelpText = "Automatically trigger CAS prune after removing the manifest to clear orphaned files.")]
	public bool Prune { get; set; } = true;
}

[Verb("add-maintainer", HelpText = "Add an authorized maintainer public key to a greenlit map")]
public class AdminAddMaintainerOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option("target-key", Required = false, HelpText = "Path to the maintainer's .rkey file.")]
	public string? TargetKey { get; set; }

	[Option('p', "pubkey", Required = false, HelpText = "Maintainer's Ed25519 public key in Base64 (alternative to --target-key).")]
	public string? PubKey { get; set; }

	[Option('k', "key", Required = false, HelpText = "Path to author or admin .rkey file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("remove-maintainer", HelpText = "Revoke an authorized maintainer public key from a greenlit map")]
public class AdminRemoveMaintainerOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option("target-key", Required = false, HelpText = "Path to the maintainer's .rkey file.")]
	public string? TargetKey { get; set; }

	[Option('p', "pubkey", Required = false, HelpText = "Maintainer's Ed25519 public key in Base64 to remove.")]
	public string? PubKey { get; set; }

	[Option('k', "key", Required = false, HelpText = "Path to author or admin .rkey file. If omitted, defaults to %appdata%\\Godot\\app_userdata\\Realm\\appdata\\keys\\authorship_key_DO-NOT-SHARE.rkey")]
	public string? Key { get; set; }

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

[Verb("list-maintainers", HelpText = "List all authorized maintainers for a map")]
public class AdminListMaintainersOptions
{
	[Option('m', "map", Required = true, HelpText = "Map title or package name")]
	public string Map { get; set; } = string.Empty;

	[Option('s', "server", Required = false, HelpText = "Registry server URL.")]
	public string? Server { get; set; }
}

public static class Program
{
	public static int Main(string[] args)
	{
		Console.OutputEncoding = Encoding.UTF8;
		string[] originalArgs = args;
		string[] sanitizedArgs = CommandLineArgsHelper.SanitizeArgs(
			args,
			typeof(AdminGreenlightOptions),
			typeof(AdminStatusOptions),
			typeof(AdminUnlockNameOptions),
			typeof(AdminInfoOptions),
			typeof(AdminExportEventsOptions),
			typeof(AdminPruneCasOptions),
			typeof(AdminDigestOptions),
			typeof(AdminSnapshotOptions),
			typeof(AdminRestoreSnapshotOptions),
			typeof(AdminRemoveManifestOptions),
			typeof(AdminAddMaintainerOptions),
			typeof(AdminRemoveMaintainerOptions),
			typeof(AdminListMaintainersOptions));

		return Parser.Default.ParseArguments<AdminGreenlightOptions, AdminStatusOptions, AdminUnlockNameOptions, AdminInfoOptions, AdminExportEventsOptions, AdminPruneCasOptions, AdminDigestOptions, AdminSnapshotOptions, AdminRestoreSnapshotOptions, AdminRemoveManifestOptions, AdminAddMaintainerOptions, AdminRemoveMaintainerOptions, AdminListMaintainersOptions>(sanitizedArgs)
			.WithParsed(options => CommandLineArgsHelper.ApplyBooleanOverrides(options, originalArgs))
			.MapResult(
				(AdminGreenlightOptions options) => ExecuteGreenlight(options),
				(AdminStatusOptions options) => ExecuteStatus(options),
				(AdminUnlockNameOptions options) => ExecuteUnlockName(options),
				(AdminInfoOptions options) => ExecuteInfo(options),
				(AdminExportEventsOptions options) => ExecuteExportEvents(options),
				(AdminPruneCasOptions options) => ExecutePruneCas(options),
				(AdminDigestOptions options) => ExecuteDigest(options),
				(AdminSnapshotOptions options) => ExecuteSnapshot(options),
				(AdminRestoreSnapshotOptions options) => ExecuteRestoreSnapshot(options),
				(AdminRemoveManifestOptions options) => ExecuteRemoveManifest(options),
				(AdminAddMaintainerOptions options) => ExecuteAddMaintainer(options),
				(AdminRemoveMaintainerOptions options) => ExecuteRemoveMaintainer(options),
				(AdminListMaintainersOptions options) => ExecuteListMaintainers(options),
				errors => 1);
	}

	private static string ResolveServerUrl(string? explicitServer)
	{
		if (!string.IsNullOrWhiteSpace(explicitServer))
		{
			return explicitServer.Trim();
		}

		return ServersConfigHelper.GetDefaultServerUrl();
	}

	private static (string privateKeyBase64, string publicKeyBase64)? ParseAdminKey(string? keyInput)
	{
		string keyPath = string.IsNullOrWhiteSpace(keyInput) ? AuthorshipKeyHelper.GetDefaultKeyPath() : keyInput.Trim();

		if (!File.Exists(keyPath))
		{
			if (string.IsNullOrWhiteSpace(keyInput))
			{
				Console.Error.WriteLine($"Error: Default key file '{keyPath}' does not exist. Please generate a key or supply a key file with --key <path>.");
			}
			else
			{
				Console.Error.WriteLine($"Error: Key file '{keyPath}' not found. Direct key strings are not allowed; please provide a path to a .rkey file.");
			}
			return null;
		}

		try
		{
			byte[] fileBytes = File.ReadAllBytes(keyPath);
			if (RkeyFile.IsRkeyBytes(fileBytes))
			{
				var keyData = RkeyFile.ParseKeyData(fileBytes);
				if (keyData == null || string.IsNullOrWhiteSpace(keyData.PrivateKey))
				{
					Console.Error.WriteLine($"Error: No private key found in key file '{keyPath}'.");
					return null;
				}

				string privBase64 = keyData.PrivateKey;
				string pubBase64 = !string.IsNullOrWhiteSpace(keyData.PublicKey)
					? keyData.PublicKey
					: AuthorSignatureHelper.GetPublicKey(privBase64);

				return (privBase64, pubBase64);
			}

			if (fileBytes.Length == 32)
			{
				string privBase64 = Convert.ToBase64String(fileBytes);
				string pubBase64 = AuthorSignatureHelper.GetPublicKey(privBase64);
				return (privBase64, pubBase64);
			}

			string textContent = File.ReadAllText(keyPath).Trim();
			if (textContent.StartsWith("{"))
			{
				var keyData = JsonSerializer.Deserialize<AuthorshipKeyData>(textContent);
				if (keyData != null && !string.IsNullOrWhiteSpace(keyData.PrivateKey))
				{
					string privBase64 = keyData.PrivateKey;
					string pubBase64 = !string.IsNullOrWhiteSpace(keyData.PublicKey)
						? keyData.PublicKey
						: AuthorSignatureHelper.GetPublicKey(privBase64);
					return (privBase64, pubBase64);
				}
			}

			textContent = textContent.Replace("-----BEGIN PRIVATE KEY-----", "")
				.Replace("-----END PRIVATE KEY-----", "")
				.Replace("\r", "")
				.Replace("\n", "")
				.Trim();

			byte[] privateBytes = Convert.FromBase64String(textContent);
			using var key = Key.Import(SignatureAlgorithm.Ed25519, privateBytes, KeyBlobFormat.RawPrivateKey, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
			byte[] publicBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);

			return (Convert.ToBase64String(privateBytes), Convert.ToBase64String(publicBytes));
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error reading key file '{keyPath}': {ex.Message}");
			return null;
		}
	}

	private static string? ParsePublicKeyFromFile(string targetKeyInput)
	{
		if (string.IsNullOrWhiteSpace(targetKeyInput))
		{
			Console.Error.WriteLine("Error: --target-key is required.");
			return null;
		}

		string targetPath = targetKeyInput.Trim();
		if (!File.Exists(targetPath))
		{
			Console.Error.WriteLine($"Error: Target key file '{targetPath}' not found. Direct key strings are not allowed; please provide a path to a .rkey file.");
			return null;
		}

		try
		{
			byte[] fileBytes = File.ReadAllBytes(targetPath);
			if (RkeyFile.IsRkeyBytes(fileBytes))
			{
				var keyData = RkeyFile.ParseKeyData(fileBytes);
				if (keyData != null && !string.IsNullOrWhiteSpace(keyData.PublicKey))
				{
					return keyData.PublicKey;
				}
				if (keyData != null && !string.IsNullOrWhiteSpace(keyData.PrivateKey))
				{
					return AuthorSignatureHelper.GetPublicKey(keyData.PrivateKey);
				}
			}

			if (fileBytes.Length == 32)
			{
				return AuthorSignatureHelper.GetPublicKey(Convert.ToBase64String(fileBytes));
			}

			string textContent = File.ReadAllText(targetPath).Trim();
			if (textContent.StartsWith("{"))
			{
				var keyData = JsonSerializer.Deserialize<AuthorshipKeyData>(textContent);
				if (keyData != null && !string.IsNullOrWhiteSpace(keyData.PublicKey))
				{
					return keyData.PublicKey;
				}
				if (keyData != null && !string.IsNullOrWhiteSpace(keyData.PrivateKey))
				{
					return AuthorSignatureHelper.GetPublicKey(keyData.PrivateKey);
				}
			}

			textContent = textContent.Replace("-----BEGIN PUBLIC KEY-----", "")
				.Replace("-----END PUBLIC KEY-----", "")
				.Replace("-----BEGIN PRIVATE KEY-----", "")
				.Replace("-----END PRIVATE KEY-----", "")
				.Replace("\r", "")
				.Replace("\n", "")
				.Trim();

			byte[] rawBytes = Convert.FromBase64String(textContent);
			if (rawBytes.Length == 32)
			{
				return textContent;
			}

			Console.Error.WriteLine($"Error: Unable to extract public key from '{targetPath}'.");
			return null;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error reading target key file '{targetPath}': {ex.Message}");
			return null;
		}
	}

	private static int CompareVersions(string? versionA, string? versionB)
	{
		if (string.IsNullOrWhiteSpace(versionA) && string.IsNullOrWhiteSpace(versionB)) return 0;
		if (string.IsNullOrWhiteSpace(versionA)) return -1;
		if (string.IsNullOrWhiteSpace(versionB)) return 1;

		string cleanA = versionA.Trim().TrimStart('v', 'V');
		string cleanB = versionB.Trim().TrimStart('v', 'V');

		if (Version.TryParse(cleanA, out var parsedA) && Version.TryParse(cleanB, out var parsedB))
		{
			return parsedA.CompareTo(parsedB);
		}

		var partsA = cleanA.Split('.');
		var partsB = cleanB.Split('.');
		int maxLength = Math.Max(partsA.Length, partsB.Length);
		for (int index = 0; index < maxLength; index++)
		{
			int numA = (index < partsA.Length && int.TryParse(partsA[index], out int parsedNumA)) ? parsedNumA : 0;
			int numB = (index < partsB.Length && int.TryParse(partsB[index], out int parsedNumB)) ? parsedNumB : 0;
			if (numA != numB) return numA.CompareTo(numB);
		}

		return string.Compare(versionA, versionB, StringComparison.OrdinalIgnoreCase);
	}

	private static int ExecuteGreenlight(AdminGreenlightOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string mapTitle = options.Map.Trim();
		string payload = $"greenlight:{mapTitle.ToLowerInvariant()}";
		string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, payload);

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Map Greenlight Override");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:     {serverUrl}");
		Console.WriteLine($"Map Title:  {mapTitle}");
		Console.WriteLine($"Admin Key:  {keyPair.Value.publicKeyBase64}");
		Console.WriteLine();

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
			var requestBody = new
			{
				MapTitle = mapTitle,
				AdminPublicKey = keyPair.Value.publicKeyBase64,
				Signature = signature
			};

			var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
			var response = httpClient.PostAsync($"{serverUrl.TrimEnd('/')}/api/admin/greenlight", content).GetAwaiter().GetResult();
			string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine($"[SUCCESS] Map '{mapTitle}' has been greenlit!");
				Console.WriteLine("The map creator may now publish new versions of this map directly through the Map Editor.");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {responseText}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteStatus(AdminStatusOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		string mapTitle = options.Map.Trim();
		string? mapVersion = string.IsNullOrWhiteSpace(options.Version) ? null : options.Version.Trim();

		if (string.IsNullOrWhiteSpace(mapVersion))
		{
			try
			{
				var client = new DistributionClient(serverUrl);
				var discoveryMaps = client.GetDiscoveryMapsAsync().GetAwaiter().GetResult();
				var matchingMaps = discoveryMaps
					.Where(m => string.Equals(m.Title, mapTitle, StringComparison.OrdinalIgnoreCase) ||
								m.MapId.StartsWith(mapTitle + "_", StringComparison.OrdinalIgnoreCase) ||
								string.Equals(m.MapId, mapTitle, StringComparison.OrdinalIgnoreCase))
					.ToList();

				if (matchingMaps.Count > 0)
				{
					var latest = matchingMaps
						.OrderByDescending(m => m.Version, Comparer<string>.Create(CompareVersions))
						.FirstOrDefault();
					if (latest != null && !string.IsNullOrWhiteSpace(latest.Version))
					{
						mapVersion = latest.Version;
					}
				}
			}
			catch
			{
			}
		}

		string endpoint = !string.IsNullOrWhiteSpace(mapVersion)
			? $"{serverUrl.TrimEnd('/')}/api/maps/greenlight_status/{Uri.EscapeDataString(mapTitle)}_{Uri.EscapeDataString(mapVersion)}"
			: $"{serverUrl.TrimEnd('/')}/api/maps/greenlight_status/{Uri.EscapeDataString(mapTitle)}";

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			var response = httpClient.GetAsync(endpoint).GetAwaiter().GetResult();
			string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (!response.IsSuccessStatusCode)
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {json}");
				return 1;
			}

			var node = JsonNode.Parse(json);
			if (node == null)
			{
				Console.Error.WriteLine("Error parsing status response.");
				return 1;
			}

			bool isGreenlit = node["isGreenlit"]?.GetValue<bool>() ?? false;
			bool adminOverride = node["adminOverride"]?.GetValue<bool>() ?? false;
			int verifiedGoodReviews = node["verifiedGoodReviewsCount"]?.GetValue<int>() ?? 0;
			int totalReviews = node["totalReviewsCount"]?.GetValue<int>() ?? 0;
			double avgRating = node["averageRating"]?.GetValue<double>() ?? 0.0;

			string titleHeader = !string.IsNullOrWhiteSpace(mapVersion)
				? $"Greenlight Status for '{mapTitle}' v{mapVersion}"
				: $"Greenlight Status for '{mapTitle}'";

			Console.WriteLine("=================================================");
			Console.WriteLine(titleHeader);
			Console.WriteLine("=================================================");
			Console.WriteLine($"Overall Greenlit:             {(isGreenlit ? "YES (Approved for Discovery)" : "NO (In Beta-Testing)")}");
			Console.WriteLine($"Admin Override:               {(adminOverride ? "ACTIVE" : "None")}");
			Console.WriteLine();
			Console.WriteLine("Greenlight Criteria:");
			Console.WriteLine($"  - Total Verified Good Reviews: {verifiedGoodReviews} / 100 (Required: >= 100)");
			Console.WriteLine();
			Console.WriteLine("Public Discovery Statistics:");
			Console.WriteLine($"  - Public Total Reviews:        {totalReviews}");
			Console.WriteLine($"  - Public Average Rating:       {avgRating:F1} / 5.0");
			Console.WriteLine("=================================================");
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteUnlockName(AdminUnlockNameOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string username = options.Username.Trim();
		string? targetKey = ParsePublicKeyFromFile(options.TargetKey);
		if (string.IsNullOrWhiteSpace(targetKey)) return 1;

		string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, $"{username}:{targetKey}");

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Unlock / Reassign Creator Name");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:     {serverUrl}");
		Console.WriteLine($"Username:   {username}");
		Console.WriteLine($"Target Key: {targetKey}");
		Console.WriteLine();

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
			string bypassToken = AdminBypassAuth.CreateBypassToken(keyPair.Value.privateKeyBase64, "admin", "override");
			httpClient.DefaultRequestHeaders.Add("X-Admin-Bypass", bypassToken);

			var requestBody = new
			{
				Username = username,
				PublicKey = targetKey,
				Signature = signature
			};

			var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
			var response = httpClient.PostAsync($"{serverUrl.TrimEnd('/')}/api/creators/register", content).GetAwaiter().GetResult();
			string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine($"[SUCCESS] Username '{username}' has been successfully reassigned to public key '{targetKey}'.");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {responseText}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteInfo(AdminInfoOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			var response = httpClient.GetAsync($"{serverUrl.TrimEnd('/')}/api/admin/info").GetAwaiter().GetResult();
			string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine("=================================================");
				Console.WriteLine("Realm Registry Server Admin Info");
				Console.WriteLine("=================================================");
				var node = JsonNode.Parse(json);
				string adminUsername = node?["adminUsername"]?.ToString()
					?? (node?["admins"] is JsonArray adminArr && adminArr.Count > 0 ? adminArr[0]?["username"]?.ToString() ?? adminArr[0]?["Username"]?.ToString() : null)
					?? "N/A";
				string adminPublicKey = node?["adminPublicKey"]?.ToString()
					?? (node?["adminPublicKeys"] is JsonArray keyArr && keyArr.Count > 0 ? keyArr[0]?.ToString() : null)
					?? (node?["admins"] is JsonArray adminArr2 && adminArr2.Count > 0 ? adminArr2[0]?["publicKey"]?.ToString() ?? adminArr2[0]?["PublicKey"]?.ToString() : null)
					?? "N/A";

				Console.WriteLine($"Admin Username:      {adminUsername}");
				Console.WriteLine($"Admin Public Key:    {adminPublicKey}");
				Console.WriteLine($"Is Graduated Admin:  {node?["isGraduatedAdmin"]?.ToString() ?? "N/A"}");
				Console.WriteLine($"Data Directory:      {node?["dataDirectory"]?.ToString() ?? "N/A"}");

				string? casFormatted = node?["casTotalSizeFormatted"]?.ToString();
				long? casBytes = null;
				if (node?["casTotalSizeBytes"] != null && long.TryParse(node["casTotalSizeBytes"]!.ToString(), out long parsedBytes))
				{
					casBytes = parsedBytes;
				}

				string casDiskSpaceText;
				if (!string.IsNullOrWhiteSpace(casFormatted) && casBytes.HasValue)
				{
					casDiskSpaceText = $"{casFormatted} ({casBytes.Value:N0} bytes)";
				}
				else if (!string.IsNullOrWhiteSpace(casFormatted))
				{
					casDiskSpaceText = casFormatted;
				}
				else if (casBytes.HasValue)
				{
					casDiskSpaceText = $"{ContentAddressableStorage.FormatByteSize(casBytes.Value)} ({casBytes.Value:N0} bytes)";
				}
				else
				{
					casDiskSpaceText = "N/A";
				}

				Console.WriteLine($"CAS Used Disk Space: {casDiskSpaceText}");
				Console.WriteLine("=================================================");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {json}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

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
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Server-Side CAS Prune");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:    {serverUrl}");
		Console.WriteLine($"Admin Key: {keyPair.Value.publicKeyBase64}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var result = client.PruneServerCasAsync(keyPair.Value.privateKeyBase64).GetAwaiter().GetResult();

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
		var keyPair = ParseAdminKey(options.Key);
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
		Console.WriteLine($"Admin Key: {keyPair.Value.publicKeyBase64}");
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
			var result = client.ApplySnapshotAsync(snapshot, keyPair.Value.privateKeyBase64).GetAwaiter().GetResult();

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

	private static int ExecuteRemoveManifest(AdminRemoveManifestOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string mapTitle = options.Map.Trim();
		string? mapVersion = string.IsNullOrWhiteSpace(options.Version) ? null : options.Version.Trim();
		bool isAllVersions = mapVersion == null;

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Remove Published Map Manifest");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:     {serverUrl}");
		Console.WriteLine($"Map Title:  {mapTitle}");
		Console.WriteLine($"Version:    {(isAllVersions ? "[ALL VERSIONS]" : mapVersion)}");
		Console.WriteLine($"Admin Key:  {keyPair.Value.publicKeyBase64}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var result = isAllVersions
				? client.RemoveAllManifestVersionsAsync(mapTitle, keyPair.Value.privateKeyBase64).GetAwaiter().GetResult()
				: client.RemoveManifestVersionAsync(mapTitle, mapVersion!, keyPair.Value.privateKeyBase64).GetAwaiter().GetResult();

			if (result.Success)
			{
				Console.WriteLine($"[SUCCESS] {result.Message}");
				Console.WriteLine($"Manifests Deleted:   {result.ManifestsDeleted}");
				Console.WriteLine($"DB Records Removed:  {result.DbRecordsRemoved}");
				if (result.DeletedManifestFiles.Count > 0)
				{
					Console.WriteLine($"Deleted Files:       {string.Join(", ", result.DeletedManifestFiles)}");
				}

				if (options.Prune)
				{
					Console.WriteLine();
					Console.WriteLine("Running subsequent CAS prune...");
					var pruneResult = client.PruneServerCasAsync(keyPair.Value.privateKeyBase64).GetAwaiter().GetResult();
					if (pruneResult.Success)
					{
						Console.WriteLine($"[SUCCESS] {pruneResult.Message}");
						if (pruneResult.TotalScanned > 0 || pruneResult.OrphansPruned > 0 || pruneResult.BytesFreed > 0)
						{
							Console.WriteLine($"Total Scanned:   {pruneResult.TotalScanned}");
							Console.WriteLine($"Orphans Pruned:  {pruneResult.OrphansPruned}");
							Console.WriteLine($"Corrupt Pruned:  {pruneResult.CorruptPruned}");
							Console.WriteLine($"Bytes Freed:     {pruneResult.BytesFreed} bytes");
						}
					}
					else
					{
						Console.Error.WriteLine($"[WARNING] Subsequent CAS prune failed: {pruneResult.Message}");
					}
				}

				Console.WriteLine("=================================================");
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
			Console.Error.WriteLine($"[ERROR] Remove manifest failed: {ex.Message}");
			return 1;
		}
	}

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
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string mapTitle = options.Map.Trim();
		string? targetPubKey = ResolveTargetPublicKey(options.TargetKey, options.PubKey);
		if (string.IsNullOrWhiteSpace(targetPubKey)) return 1;

		string payload = $"add_maintainer:{mapTitle.ToLowerInvariant()}:{targetPubKey.Trim()}";
		string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, payload);

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Add Map Maintainer");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:          {serverUrl}");
		Console.WriteLine($"Map:             {mapTitle}");
		Console.WriteLine($"Maintainer Key:  {targetPubKey}");
		Console.WriteLine($"Requester Key:   {keyPair.Value.publicKeyBase64}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var request = new AddMapMaintainerRequest
			{
				MapTitle = mapTitle,
				MaintainerPublicKey = targetPubKey,
				RequesterPublicKey = keyPair.Value.publicKeyBase64,
				Signature = signature
			};

			var response = client.AddMapMaintainerAsync(request).GetAwaiter().GetResult();
			if (response.Success)
			{
				Console.WriteLine($"[SUCCESS] {response.Message}");
				Console.WriteLine($"Owner:              {response.OwnerPublicKey}");
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
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Add maintainer failed: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteRemoveMaintainer(AdminRemoveMaintainerOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string mapTitle = options.Map.Trim();
		string? targetPubKey = ResolveTargetPublicKey(options.TargetKey, options.PubKey);
		if (string.IsNullOrWhiteSpace(targetPubKey)) return 1;

		string payload = $"remove_maintainer:{mapTitle.ToLowerInvariant()}:{targetPubKey.Trim()}";
		string signature = AuthorSignatureHelper.SignMessage(keyPair.Value.privateKeyBase64, payload);

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Remove Map Maintainer");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:          {serverUrl}");
		Console.WriteLine($"Map:             {mapTitle}");
		Console.WriteLine($"Maintainer Key:  {targetPubKey}");
		Console.WriteLine($"Requester Key:   {keyPair.Value.publicKeyBase64}");
		Console.WriteLine();

		try
		{
			var client = new DistributionClient(serverUrl);
			var request = new RemoveMapMaintainerRequest
			{
				MapTitle = mapTitle,
				MaintainerPublicKey = targetPubKey,
				RequesterPublicKey = keyPair.Value.publicKeyBase64,
				Signature = signature
			};

			var response = client.RemoveMapMaintainerAsync(request).GetAwaiter().GetResult();
			if (response.Success)
			{
				Console.WriteLine($"[SUCCESS] {response.Message}");
				Console.WriteLine($"Owner:              {response.OwnerPublicKey}");
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
			if (response.Success)
			{
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
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] List maintainers failed: {ex.Message}");
			return 1;
		}
	}
}
