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
	private static int ExecuteRenameAccount(AdminRenameAccountOptions options)
	{
		if (options.AccountId <= 0)
		{
			Console.Error.WriteLine("Error: --account-id must be the positive decimal AccountId of an existing account.");
			return 1;
		}

		if (!DisplayNameRules.TryCanonicalize(options.DisplayName, out string canonical, out DisplayNameError nameError))
		{
			Console.Error.WriteLine($"Error: {DisplayNameRules.DescribeError(nameError)}");
			return 1;
		}

		string serverUrl = ResolveServerUrl(options.Server);
		using var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string nonce = Guid.NewGuid().ToString("N");
		long timestampUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
		string signature = keyPair.CreateSignature(AccountAdminRenameProof.BuildPayload(options.AccountId, canonical, nonce, timestampUnixSeconds));

		Console.WriteLine("=================================================");
		Console.WriteLine("Realm Admin Tool - Rename Account");
		Console.WriteLine("=================================================");
		Console.WriteLine($"Server:      {serverUrl}");
		Console.WriteLine($"AccountId:   {options.AccountId}");
		Console.WriteLine($"DisplayName: {canonical}");
		Console.WriteLine();

		try
		{
			using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
			var requestBody = new
			{
				AccountId = options.AccountId,
				DisplayName = canonical,
				Nonce = nonce,
				TimestampUnixSeconds = timestampUnixSeconds,
				Signature = signature,
				AdminPublicKey = keyPair.PublicKeyBase64
			};

			var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
			var response = httpClient.PostAsync($"{serverUrl.TrimEnd('/')}/auth/admin/rename", content).GetAwaiter().GetResult();
			string responseText = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

			if (response.IsSuccessStatusCode)
			{
				Console.WriteLine($"[SUCCESS] Account {options.AccountId} is now '{canonical}'.");
				return 0;
			}

			Console.Error.WriteLine($"[FAILED] HTTP {(int)response.StatusCode}: {responseText}");
			return 1;
		}
		catch (HttpRequestException exception)
		{
			Console.Error.WriteLine($"[FAILED] {exception.Message}");
			return 1;
		}
	}

	private static int ExecuteUnlockName(AdminUnlockNameOptions options)
	{
		string serverUrl = ResolveServerUrl(options.Server);
		using var keyPair = ParseAdminKey(options.Key);
		if (keyPair == null) return 1;

		string username = options.Username.Trim();
		string? targetKey = ParsePublicKeyFromFile(options.TargetKey);
		if (string.IsNullOrWhiteSpace(targetKey)) return 1;

		string signature = keyPair.CreateSignature(CreatorRegistrationProof.BuildPayload(username, targetKey, null, null));

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
			string bypassToken = keyPair.CreateBypassToken("admin", "override");
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
}
