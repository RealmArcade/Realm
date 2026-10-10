using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public static class AuthorshipKeyHelper
{
	public const string DefaultKeyFileName = "authorship_key_DO-NOT-SHARE.rkey";
	public const string LegacyKeyFileName = "authorship_key.pem";

	public static string GetDefaultKeysDirectory()
	{
		return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Godot",
			"app_userdata",
			"Realm",
			"appdata",
			"keys"
		);
	}

	public static string GetDefaultKeyPath(string? customDirectory = null)
	{
		string dir = string.IsNullOrWhiteSpace(customDirectory) ? GetDefaultKeysDirectory() : customDirectory;
		return Path.Combine(dir, DefaultKeyFileName);
	}

	public static (Key Key, AuthorshipKeyData Data, string Path, bool CreatedNew) GetOrGenerateKeyInfo(string? keyDirectory = null, string defaultUserName = "")
	{
		string dir = string.IsNullOrWhiteSpace(keyDirectory) ? GetDefaultKeysDirectory() : keyDirectory;
		if (!Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		string keyPath = Path.Combine(dir, DefaultKeyFileName);
		string legacyPath = Path.Combine(dir, LegacyKeyFileName);

		if (TryLoadKey(keyPath, out Key? key, out AuthorshipKeyData? data))
		{
			return (key!, data!, keyPath, false);
		}

		if (TryLoadLegacyKey(legacyPath, keyPath, defaultUserName, out key, out data))
		{
			return (key!, data!, keyPath, false);
		}

		return GenerateNewKey(keyPath, defaultUserName);
	}

	private static bool TryLoadKey(string keyPath, out Key? key, out AuthorshipKeyData? data)
	{
		key = null;
		data = null;

		if (!File.Exists(keyPath))
		{
			return false;
		}

		try
		{
			byte[] fileBytes = File.ReadAllBytes(keyPath);
			var keyData = RkeyFile.ParseKeyData(fileBytes);
			if (keyData == null || string.IsNullOrWhiteSpace(keyData.PrivateKey))
			{
				return false;
			}

			byte[] privBytes = Convert.FromBase64String(keyData.PrivateKey);
			key = Key.Import(
				SignatureAlgorithm.Ed25519,
				privBytes,
				KeyBlobFormat.RawPrivateKey,
				new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport }
			);

			if (string.IsNullOrWhiteSpace(keyData.PublicKey))
			{
				byte[] pubBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
				keyData.PublicKey = Convert.ToBase64String(pubBytes);
			}

			data = keyData;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryLoadLegacyKey(string legacyPath, string keyPath, string defaultUserName, out Key? key, out AuthorshipKeyData? data)
	{
		key = null;
		data = null;

		if (!File.Exists(legacyPath))
		{
			return false;
		}

		try
		{
			byte[] legacyBytes = File.ReadAllBytes(legacyPath);
			key = Key.Import(
				SignatureAlgorithm.Ed25519,
				legacyBytes,
				KeyBlobFormat.RawPrivateKey,
				new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport }
			);
			byte[] privBytes = key.Export(KeyBlobFormat.RawPrivateKey);
			byte[] pubBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
			data = new AuthorshipKeyData
			{
				UserName = defaultUserName,
				PublicKey = Convert.ToBase64String(pubBytes),
				PrivateKey = Convert.ToBase64String(privBytes)
			};
			byte[] rkeyBytes = RkeyFile.Build(data.UserName, data.PublicKey, data.PrivateKey);
			File.WriteAllBytes(keyPath, rkeyBytes);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static (Key Key, AuthorshipKeyData Data, string Path, bool CreatedNew) GenerateNewKey(string keyPath, string defaultUserName)
	{
		var newKey = Key.Create(
			SignatureAlgorithm.Ed25519,
			new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport }
		);
		byte[] newPrivBytes = newKey.Export(KeyBlobFormat.RawPrivateKey);
		byte[] newPubBytes = newKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

		var newData = new AuthorshipKeyData
		{
			UserName = defaultUserName,
			PublicKey = Convert.ToBase64String(newPubBytes),
			PrivateKey = Convert.ToBase64String(newPrivBytes)
		};

		byte[] newRkeyBytes = RkeyFile.Build(newData.UserName, newData.PublicKey, newData.PrivateKey);
		File.WriteAllBytes(keyPath, newRkeyBytes);

		return (newKey, newData, keyPath, true);
	}

	public static Key GetOrGenerateAuthorshipKey(string? keyDirectory = null, string defaultUserName = "")
	{
		return GetOrGenerateKeyInfo(keyDirectory, defaultUserName).Key;
	}

	public static void UpdateUserName(string? keyDirectory, string newUserName)
	{
		string dir = string.IsNullOrWhiteSpace(keyDirectory) ? GetDefaultKeysDirectory() : keyDirectory;
		string keyPath = Path.Combine(dir, DefaultKeyFileName);
		
		if (!File.Exists(keyPath))
		{
			return;
		}

		try
		{
			byte[] fileBytes = File.ReadAllBytes(keyPath);
			var keyData = RkeyFile.ParseKeyData(fileBytes);
			if (keyData == null)
			{
				return;
			}
			
			keyData.UserName = newUserName;
			byte[] updatedBytes = RkeyFile.Build(keyData.UserName, keyData.PublicKey, keyData.PrivateKey);
			File.WriteAllBytes(keyPath, updatedBytes);
		}
		catch
		{
		}
	}
}
