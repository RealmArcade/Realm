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
			typeof(AdminListMaintainersOptions),
			typeof(AdminRenameAccountOptions),
			typeof(AdminKeygenOptions));

		return Parser.Default.ParseArguments<AdminGreenlightOptions, AdminStatusOptions, AdminUnlockNameOptions, AdminInfoOptions, AdminExportEventsOptions, AdminPruneCasOptions, AdminDigestOptions, AdminSnapshotOptions, AdminRestoreSnapshotOptions, AdminRemoveManifestOptions, AdminAddMaintainerOptions, AdminRemoveMaintainerOptions, AdminListMaintainersOptions, AdminRenameAccountOptions, AdminKeygenOptions>(sanitizedArgs)
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
				(AdminRenameAccountOptions options) => ExecuteRenameAccount(options),
				(AdminKeygenOptions options) => ExecuteKeygen(options),
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

	private static AdminKeySession? ParseAdminKey(string? keyInput)
	{
		string keyPath = string.IsNullOrWhiteSpace(keyInput) ? AuthorshipKeyHelper.GetDefaultAdminKeyPath() : keyInput.Trim();

		if (!File.Exists(keyPath))
		{
			Console.Error.WriteLine(string.IsNullOrWhiteSpace(keyInput)
				? $"Error: Default admin key file '{keyPath}' does not exist. Generate one with 'Realm.Tools.Admin keygen' or supply a key file with --key <path>."
				: $"Error: Key file '{keyPath}' not found. Direct key strings are not allowed; please provide a path to an encrypted .rkey file.");
			return null;
		}

		string keyFileName = Path.GetFileName(keyPath);
		if (!string.Equals(keyFileName, AuthorshipKeyHelper.DefaultAdminKeyFileName, StringComparison.OrdinalIgnoreCase))
		{
			Console.Error.WriteLine($"Error: Admin key files must be named '{AuthorshipKeyHelper.DefaultAdminKeyFileName}'; '{keyFileName}' is not allowed, regardless of its folder.");
			return null;
		}

		try
		{
			return LoadEncryptedAdminKey(keyPath);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error reading key file '{keyPath}': {ex.Message}");
			return null;
		}
	}

	private static AdminKeySession? LoadEncryptedAdminKey(string keyPath)
	{
		byte[] fileBytes = File.ReadAllBytes(keyPath);
		if (!RkeyFile.IsRkeyBytes(fileBytes))
		{
			Console.Error.WriteLine($"Error: '{keyPath}' is not a valid .rkey container. Generate an admin key with 'Realm.Tools.Admin keygen'.");
			return null;
		}

		var keyData = RkeyFile.ParseKeyData(fileBytes);
		if (keyData == null || keyData.EncryptedPrivateKey == null || string.IsNullOrWhiteSpace(keyData.EncryptedPrivateKey.CiphertextBase64))
		{
			Console.Error.WriteLine($"Error: The PrivateKey in '{keyPath}' is not encrypted. Admin keys must keep the PrivateKey encrypted; generate a new admin key with 'Realm.Tools.Admin keygen' before using it.");
			return null;
		}

		string password = PasswordPrompt.Read("Admin key password: ");
		if (string.IsNullOrEmpty(password))
		{
			Console.Error.WriteLine("Error: The admin key password is required.");
			return null;
		}

		try
		{
			byte[] privateKeyBytes = PrivateKeyEncryptionHelper.DecryptPrivateKeyBytes(keyData.EncryptedPrivateKey, password);
			string publicKeyBase64 = !string.IsNullOrWhiteSpace(keyData.PublicKey)
				? keyData.PublicKey
				: AuthorSignatureHelper.GetPublicKey(privateKeyBytes);

			return new AdminKeySession(privateKeyBytes, publicKeyBase64);
		}
		catch (PrivateKeyDecryptionException ex)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			return null;
		}
	}

	private class AdminKeySession : IDisposable
	{
		private byte[]? _privateKeyBytes;

		public AdminKeySession(byte[] privateKeyBytes, string publicKeyBase64)
		{
			_privateKeyBytes = privateKeyBytes;
			PublicKeyBase64 = publicKeyBase64;
		}

		public string PublicKeyBase64 { get; }

		public string CreateSignature(string message)
		{
			return AuthorSignatureHelper.SignMessage(GetPrivateKeyBytes(), message);
		}

		public string CreateBypassToken(string resource, string action)
		{
			return AdminBypassAuth.CreateBypassToken(GetPrivateKeyBytes(), resource, action);
		}

		public string ExportPrivateKeyBase64()
		{
			return Convert.ToBase64String(GetPrivateKeyBytes());
		}

		public void Dispose()
		{
			if (_privateKeyBytes == null) return;
			CryptographicOperations.ZeroMemory(_privateKeyBytes);
			_privateKeyBytes = null;
		}

		private ReadOnlySpan<byte> GetPrivateKeyBytes()
		{
			return _privateKeyBytes ?? throw new ObjectDisposedException(nameof(AdminKeySession));
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
			return TryParsePublicKeyFromFileInternal(targetPath);
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error reading target key file '{targetPath}': {ex.Message}");
			return null;
		}
	}

	private static string? TryParsePublicKeyFromFileInternal(string targetPath)
	{
		byte[] fileBytes = File.ReadAllBytes(targetPath);

		if (RkeyFile.IsRkeyBytes(fileBytes))
		{
			return ParseRkeyPublicKey(fileBytes);
		}

		if (fileBytes.Length == 32)
		{
			return AuthorSignatureHelper.GetPublicKey(Convert.ToBase64String(fileBytes));
		}

		string textContent = File.ReadAllText(targetPath).Trim();
		if (textContent.StartsWith("{"))
		{
			var jsonResult = ParseJsonPublicKey(textContent);
			if (jsonResult != null) return jsonResult;
		}

		return ParsePemPublicKey(textContent, targetPath);
	}

	private static string? ParseRkeyPublicKey(byte[] fileBytes)
	{
		var keyData = RkeyFile.ParseKeyData(fileBytes);
		if (keyData == null) return null;

		if (!string.IsNullOrWhiteSpace(keyData.PublicKey)) return keyData.PublicKey;
		if (!string.IsNullOrWhiteSpace(keyData.PrivateKey)) return AuthorSignatureHelper.GetPublicKey(keyData.PrivateKey);

		return null;
	}

	private static string? ParseJsonPublicKey(string textContent)
	{
		var keyData = JsonSerializer.Deserialize<AuthorshipKeyData>(textContent);
		if (keyData == null) return null;

		if (!string.IsNullOrWhiteSpace(keyData.PublicKey)) return keyData.PublicKey;
		if (!string.IsNullOrWhiteSpace(keyData.PrivateKey)) return AuthorSignatureHelper.GetPublicKey(keyData.PrivateKey);

		return null;
	}

	private static string? ParsePemPublicKey(string textContent, string targetPath)
	{
		textContent = textContent.Replace("-----BEGIN PUBLIC KEY-----", "")
			.Replace("-----END PUBLIC KEY-----", "")
			.Replace("-----BEGIN PRIVATE KEY-----", "")
			.Replace("-----END PRIVATE KEY-----", "")
			.Replace("\r", "")
			.Replace("\n", "")
			.Trim();

		byte[] rawBytes = Convert.FromBase64String(textContent);
		if (rawBytes.Length == 32) return textContent;

		Console.Error.WriteLine($"Error: Unable to extract public key from '{targetPath}'.");
		return null;
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

		return CompareVersionParts(cleanA, cleanB, versionA, versionB);
	}

	private static int CompareVersionParts(string cleanA, string cleanB, string originalA, string originalB)
	{
		var partsA = cleanA.Split('.');
		var partsB = cleanB.Split('.');
		int maxLength = Math.Max(partsA.Length, partsB.Length);

		for (int index = 0; index < maxLength; index++)
		{
			int numA = ParseVersionPart(partsA, index);
			int numB = ParseVersionPart(partsB, index);
			if (numA != numB) return numA.CompareTo(numB);
		}

		return string.Compare(originalA, originalB, StringComparison.OrdinalIgnoreCase);
	}

	private static int ParseVersionPart(string[] parts, int index)
	{
		if (index >= parts.Length) return 0;
		return int.TryParse(parts[index], out int parsedNum) ? parsedNum : 0;
	}
}
