using System;
using System.Collections.Generic;
using System.IO;
using NSec.Cryptography;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public static class AuthorshipKeyHelper
{
	public const string AuthorshipKeyFilePrefix = "authorship_key_DO-NOT-SHARE";
	public const string AuthorshipKeyFileExtension = ".rkey";
	public const string DefaultAuthorshipKeyFileName = "authorship_key_DO-NOT-SHARE.rkey";
	public const string DefaultAdminKeyFileName = "server_admin_key_DO-NOT-SHARE.rkey";

	public static string GetDefaultKeysDirectory(string? customDirectory = null)
	{
		if (!string.IsNullOrWhiteSpace(customDirectory))
		{
			return customDirectory;
		}

		return Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"Godot",
			"app_userdata",
			"Realm",
			"appdata",
			"keys"
		);
	}

	public static string GetDefaultAdminKeyPath(string? customDirectory = null)
	{
		return Path.Combine(GetDefaultKeysDirectory(customDirectory), DefaultAdminKeyFileName);
	}

	public static string GetAuthorshipKeyFileName(string? userName)
	{
		return $"{AuthorshipKeyFilePrefix}_{NormalizeUserName(userName)}{AuthorshipKeyFileExtension}";
	}

	public static string GetDefaultAuthorshipKeyPath(string? customDirectory = null, string? userName = null)
	{
		return Path.Combine(GetDefaultKeysDirectory(customDirectory), GetAuthorshipKeyFileName(userName));
	}

	public static string GetAuthorshipKeyUserName(string keyPath)
	{
		string fileName = Path.GetFileName(keyPath);
		if (!fileName.StartsWith(AuthorshipKeyFilePrefix + "_", StringComparison.OrdinalIgnoreCase)) return string.Empty;
		if (!fileName.EndsWith(AuthorshipKeyFileExtension, StringComparison.OrdinalIgnoreCase)) return string.Empty;

		string userName = fileName[(AuthorshipKeyFilePrefix.Length + 1)..^AuthorshipKeyFileExtension.Length];
		return string.Equals(userName, NormalizeUserName(userName), StringComparison.Ordinal) ? userName : string.Empty;
	}

	public static bool TryReadKeyFile(string keyPath, out AuthorshipKeyData? data)
	{
		data = null;

		string fileNameUserName = GetAuthorshipKeyUserName(keyPath);
		if (fileNameUserName.Length == 0 || !File.Exists(keyPath))
		{
			return false;
		}

		try
		{
			var keyData = RkeyFile.ParseKeyData(File.ReadAllBytes(keyPath));
			if (keyData == null || string.IsNullOrWhiteSpace(keyData.PrivateKey))
			{
				return false;
			}

			if (!string.Equals(keyData.UserName, fileNameUserName, StringComparison.Ordinal))
			{
				return false;
			}

			data = keyData;
			return true;
		}
		catch
		{
			return false;
		}
	}

	public static List<AuthorshipKeyInfo> EnumerateAuthorshipKeys(string? keyDirectory = null)
	{
		var keys = new List<AuthorshipKeyInfo>();
		string dir = GetDefaultKeysDirectory(keyDirectory);
		if (!Directory.Exists(dir))
		{
			return keys;
		}

		foreach (string keyPath in Directory.GetFiles(dir, AuthorshipKeyFilePrefix + "_*" + AuthorshipKeyFileExtension))
		{
			if (TryReadKeyFile(keyPath, out AuthorshipKeyData? data) && data != null)
			{
				keys.Add(new AuthorshipKeyInfo(keyPath, data));
			}
		}

		keys.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
		return keys;
	}

	public static (NSec.Cryptography.Key Key, AuthorshipKeyData Data, string Path, bool CreatedNew) GetOrGenerateKeyInfo(string? keyDirectory = null, string defaultUserName = "")
	{
		string dir = GetDefaultKeysDirectory(keyDirectory);
		if (!Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		string userName = NormalizeUserName(defaultUserName);
		if (userName.Length == 0)
		{
			throw new InvalidOperationException("An authorship key needs a user name to be created or loaded.");
		}

		string keyPath = Path.Combine(dir, GetAuthorshipKeyFileName(userName));
		if (TryLoadKey(keyPath, out Key? key, out AuthorshipKeyData? data))
		{
			return (key!, data!, keyPath, false);
		}

		return GenerateNewKey(keyPath, userName);
	}

	public static Key GetOrGenerateAuthorshipKey(string? keyDirectory = null, string defaultUserName = "")
	{
		return GetOrGenerateKeyInfo(keyDirectory, defaultUserName).Key;
	}

	public static bool TryLoadAuthorshipKey(string keyPath, out Key? key, out AuthorshipKeyData? data)
	{
		return TryLoadKey(keyPath, out key, out data);
	}

	private static bool TryLoadKey(string keyPath, out Key? key, out AuthorshipKeyData? data)
	{
		key = null;
		data = null;

		if (!TryReadKeyFile(keyPath, out AuthorshipKeyData? keyData) || keyData == null)
		{
			return false;
		}

		try
		{
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

	private static (Key Key, AuthorshipKeyData Data, string Path, bool CreatedNew) GenerateNewKey(string keyPath, string userName)
	{
		var newKey = Key.Create(
			SignatureAlgorithm.Ed25519,
			new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport }
		);
		byte[] newPrivBytes = newKey.Export(KeyBlobFormat.RawPrivateKey);
		byte[] newPubBytes = newKey.PublicKey.Export(KeyBlobFormat.RawPublicKey);

		var newData = new AuthorshipKeyData
		{
			UserName = userName,
			PublicKey = Convert.ToBase64String(newPubBytes),
			PrivateKey = Convert.ToBase64String(newPrivBytes)
		};

		byte[] newRkeyBytes = RkeyFile.Build(newData.UserName, newData.PublicKey, newData.PrivateKey);
		File.WriteAllBytes(keyPath, newRkeyBytes);

		return (newKey, newData, keyPath, true);
	}

	private static string NormalizeUserName(string? userName)
	{
		if (string.IsNullOrWhiteSpace(userName)) return string.Empty;

		string trimmed = userName.Trim();
		foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
		{
			trimmed = trimmed.Replace(invalidCharacter, '_');
		}

		return trimmed;
	}
}

public readonly record struct AuthorshipKeyInfo(string Path, AuthorshipKeyData Data)
{
	public string UserName => Data.UserName;
}
