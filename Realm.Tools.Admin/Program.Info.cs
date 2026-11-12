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
	private static int ExecuteInfo(AdminInfoOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		using var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			httpClient.DefaultRequestHeaders.Add(AdminBypassAuth.BypassTokenHeaderName, keyPair.CreateBypassToken("admin", "info"));
			var response = httpClient.GetAsync($"{serverUrl.TrimEnd('/')}/api/admin/info").GetAwaiter().GetResult();
			string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (!response.IsSuccessStatusCode)
			{
				Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {json}");
				return 1;
			}

			PrintAdminInfo(json);
			return 0;
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[ERROR] Connection failed: {ex.Message}");
			return 1;
		}
	}

	private static void PrintAdminInfo(string json)
	{
		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Registry Server Admin Info");
		Console.WriteLine("=================================================");
		var node = JsonNode.Parse(json);

		string adminUsername = ExtractAdminUsername(node);
		string adminPublicKey = ExtractAdminPublicKey(node);

		Console.WriteLine($"Admin Username:      {adminUsername}");
		Console.WriteLine($"Admin Public Key:    {adminPublicKey}");
		Console.WriteLine($"Is Graduated Admin:  {node?["isGraduatedAdmin"]?.ToString() ?? "N/A"}");
		Console.WriteLine($"Data Directory:      {node?["dataDirectory"]?.ToString() ?? "N/A"}");

		Console.WriteLine($"CAS Used Disk Space: {ExtractCasDiskSpaceText(node)}");
		Console.WriteLine("=================================================");
	}

	private static string ExtractAdminUsername(JsonNode? node)
	{
		if (node?["adminUsername"] != null) return node["adminUsername"]!.ToString();

		if (node?["admins"] is JsonArray adminArr && adminArr.Count > 0)
		{
			if (adminArr[0]?["username"] != null) return adminArr[0]!["username"]!.ToString();
			if (adminArr[0]?["Username"] != null) return adminArr[0]!["Username"]!.ToString();
		}

		return "N/A";
	}

	private static string ExtractAdminPublicKey(JsonNode? node)
	{
		if (node is not JsonObject jsonObj) return "N/A";

		if (jsonObj.TryGetPropertyValue("adminPublicKey", out JsonNode? adminPublicKey) && adminPublicKey != null)
		{
			return adminPublicKey.ToString();
		}

		if (TryExtractFromAdminPublicKeys(jsonObj, out string key))
		{
			return key;
		}

		if (TryExtractFromAdminsArray(jsonObj, out key))
		{
			return key;
		}

		return "N/A";
	}

	private static bool TryExtractFromAdminPublicKeys(JsonObject node, out string key)
	{
		key = string.Empty;
		if (!node.TryGetPropertyValue("adminPublicKeys", out JsonNode? arrNode)) return false;
		if (arrNode is not JsonArray keyArr) return false;
		if (keyArr.Count == 0) return false;

		JsonNode? firstKey = keyArr[0];
		if (firstKey == null) return false;

		key = firstKey.ToString();
		return true;
	}

	private static bool TryExtractFromAdminsArray(JsonObject node, out string key)
	{
		key = string.Empty;
		if (!node.TryGetPropertyValue("admins", out JsonNode? arrNode)) return false;
		if (arrNode is not JsonArray adminArr) return false;
		if (adminArr.Count == 0) return false;

		if (adminArr[0] is not JsonObject firstAdminObj) return false;

		if (firstAdminObj.TryGetPropertyValue("publicKey", out JsonNode? pubKey) && pubKey != null)
		{
			key = pubKey.ToString();
			return true;
		}

		if (firstAdminObj.TryGetPropertyValue("PublicKey", out JsonNode? pubKey2) && pubKey2 != null)
		{
			key = pubKey2.ToString();
			return true;
		}

		return false;
	}

	private static string ExtractCasDiskSpaceText(JsonNode? node)
	{
		string? casFormatted = node?["casTotalSizeFormatted"]?.ToString();

		long? casBytes = null;
		if (node?["casTotalSizeBytes"] != null && long.TryParse(node["casTotalSizeBytes"]!.ToString(), out long parsedBytes))
		{
			casBytes = parsedBytes;
		}

		if (!string.IsNullOrWhiteSpace(casFormatted) && casBytes.HasValue) return $"{casFormatted} ({casBytes.Value:N0} bytes)";
		if (!string.IsNullOrWhiteSpace(casFormatted)) return casFormatted;
		if (casBytes.HasValue) return $"{ContentAddressableStorage.FormatByteSize(casBytes.Value)} ({casBytes.Value:N0} bytes)";
		return "N/A";
	}
}
