using System;
using System.Collections.Generic;
using System.IO;

namespace Realm.Client.Services;

public sealed class EditorAssetKeys
{
	private readonly Dictionary<string, string> _cache;

	public EditorAssetKeys(Dictionary<string, string> cache)
	{
		_cache = cache;
	}

	private static string ApplyModelAssetExtension(string filename)
	{
		if (filename.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
		{
			return Path.GetFileNameWithoutExtension(filename) + ".rmesh";
		}

		if (filename.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || filename.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
		{
			return filename;
		}

		if (!filename.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			return filename + ".rmesh";
		}

		return filename;
	}

	public string NormalizeModelAssetKey(string pathOrId)
	{
		if (string.IsNullOrEmpty(pathOrId)) return "";
		if (_cache.TryGetValue(pathOrId, out string? cached))
		{
			return cached;
		}

		string trimmed = pathOrId.Trim().Replace('\\', '/');
		if (trimmed.Contains('/'))
		{
			string prefix = trimmed.Substring(0, trimmed.IndexOf('/'));
			if (AssetIndexService.ValidAssetPrefixes.Contains(prefix))
			{
				string lower = trimmed.ToLowerInvariant();
				_cache[pathOrId] = lower;
				return lower;
			}
		}

		string filename = Path.GetFileName(trimmed);
		filename = ApplyModelAssetExtension(filename);

		string result = filename.ToLowerInvariant();
		_cache[pathOrId] = result;
		return result;
	}
}
