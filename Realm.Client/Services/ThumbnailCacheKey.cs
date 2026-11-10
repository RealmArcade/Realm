using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Realm.Shared;
using Realm.Shared.Distribution;

namespace Realm.Client.Services;

/// <summary>
///     Names a model's cached thumbnail. The key is the model's content hash where one is known or embedded, and a
///     size/timestamp digest where none is, so a cache entry follows the bytes it was drawn from without needing the
///     renderer that produced it.
/// </summary>
public static class ThumbnailCacheKey
{
	public static string NormalizePath(string path)
	{
		if (string.IsNullOrEmpty(path)) return string.Empty;
		return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
	}

	public static string GetBlake3(string filePath, string? preferredBlake3 = null)
	{
		if (!string.IsNullOrEmpty(preferredBlake3))
		{
			return ContentAddressableStorage.NormalizeBlake3Hash(preferredBlake3);
		}

		string fileName = Path.GetFileNameWithoutExtension(filePath);
		if (fileName.Length == 64 && IsHexOnly(fileName))
		{
			return fileName.ToLowerInvariant();
		}

		string? metadataB3 = ExtractBlake3FromMetadata(filePath);
		if (!string.IsNullOrEmpty(metadataB3))
		{
			return metadataB3;
		}

		string? fallbackHash = ComputeFallbackHash(filePath);
		if (!string.IsNullOrEmpty(fallbackHash))
		{
			return fallbackHash;
		}

		return ContentAddressableStorage.NormalizeBlake3Hash(fileName);
	}

	private static string? ExtractBlake3FromMetadata(string filePath)
	{
		try
		{
			string? metaJson = RealmMetadataHelper.ExtractMetadata(filePath);
			if (string.IsNullOrEmpty(metaJson))
				return null;

			var node = JsonNode.Parse(metaJson);
			string? b3 = node?["blake3"]?.ToString();
			if (!string.IsNullOrEmpty(b3))
			{
				return ContentAddressableStorage.NormalizeBlake3Hash(b3);
			}
		}
		catch
		{
		}

		return null;
	}

	private static string? ComputeFallbackHash(string filePath)
	{
		try
		{
			if (!File.Exists(filePath))
				return null;

			var fi = new FileInfo(filePath);
			string key = $"{filePath}_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}";
			using var sha = SHA256.Create();
			byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
			return Convert.ToHexString(hashBytes).ToLowerInvariant();
		}
		catch
		{
		}

		return null;
	}

	private static bool IsHexOnly(string str)
	{
		foreach (char c in str)
		{
			if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
			{
				return false;
			}
		}

		return true;
	}
}
