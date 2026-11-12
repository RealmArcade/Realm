using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.Services;

public static class CasAssetImageLoader
{
	private static readonly HttpClient SharedHttpClient = new()
	{
		Timeout = TimeSpan.FromSeconds(10)
	};

	private static readonly ConcurrentDictionary<string, Texture2D> TextureCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, Task<Texture2D?>> InFlightLoads = new(StringComparer.OrdinalIgnoreCase);

	public static async Task ApplyTextureAsync(
		TextureRect target,
		string? hash,
		string? serverUrl,
		string? fallbackPath = null)
	{
		if (!GodotObject.IsInstanceValid(target))
		{
			return;
		}

		Texture2D? texture = await LoadTextureAsync(hash, serverUrl, fallbackPath);
		if (texture != null && GodotObject.IsInstanceValid(target))
		{
			target.Texture = texture;
		}
	}

	public static async Task<Texture2D?> LoadTextureAsync(
		string? hash,
		string? serverUrl,
		string? fallbackPath = null)
	{
		string normalizedHash = string.IsNullOrWhiteSpace(hash)
			? string.Empty
			: ContentAddressableStorage.NormalizeBlake3Hash(hash);

		if (!string.IsNullOrEmpty(normalizedHash) && TextureCache.TryGetValue(normalizedHash, out Texture2D? cached))
		{
			return cached;
		}

		Texture2D? localTexture = TryLoadLocalTexture(normalizedHash, fallbackPath);
		if (localTexture != null)
		{
			if (!string.IsNullOrEmpty(normalizedHash))
			{
				TextureCache[normalizedHash] = localTexture;
			}

			return localTexture;
		}

		if (string.IsNullOrEmpty(normalizedHash) || string.IsNullOrWhiteSpace(serverUrl))
		{
			return null;
		}

		if (InFlightLoads.TryGetValue(normalizedHash, out Task<Texture2D?>? existingTask))
		{
			return await existingTask;
		}

		var loadTask = DownloadAndCacheTextureAsync(normalizedHash, serverUrl.TrimEnd('/'));
		var registered = InFlightLoads.GetOrAdd(normalizedHash, loadTask);
		try
		{
			return await registered;
		}
		finally
		{
			InFlightLoads.TryRemove(normalizedHash, out _);
		}
	}

	public static Texture2D? TryLoadLocalTexture(string? hash, string? fallbackPath = null)
	{
		if (!string.IsNullOrWhiteSpace(hash))
		{
			string? casPath = MapAssetManager.Storage.FindAssetFilePath(hash);
			if (!string.IsNullOrEmpty(casPath) && File.Exists(casPath))
			{
				Texture2D? fromCas = LoadTextureFromFile(casPath);
				if (fromCas != null)
				{
					return fromCas;
				}
			}
		}

		if (string.IsNullOrWhiteSpace(fallbackPath))
		{
			return null;
		}

		if (fallbackPath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
		{
			return ResourceLoader.Exists(fallbackPath)
				? TrimmedTextureLoader.LoadTrimmedTexture(fallbackPath)
				: null;
		}

		return File.Exists(fallbackPath) ? LoadTextureFromFile(fallbackPath) : null;
	}

	private static async Task<Texture2D?> DownloadAndCacheTextureAsync(string normalizedHash, string baseUrl)
	{
		try
		{
			string url = $"{baseUrl}/api/assets/{Uri.EscapeDataString(normalizedHash)}";
			using HttpResponseMessage response = await SharedHttpClient.GetAsync(url);
			if (!response.IsSuccessStatusCode)
			{
				return null;
			}

			byte[] bytes = await response.Content.ReadAsByteArrayAsync();
			if (bytes.Length == 0)
			{
				return null;
			}

			string? extension = DetectImageExtension(bytes);
			if (extension == null)
			{
				return null;
			}

			var image = new Image();
			Error err = extension switch
			{
				".webp" => image.LoadWebpFromBuffer(bytes),
				".png" => image.LoadPngFromBuffer(bytes),
				".jpg" => image.LoadJpgFromBuffer(bytes),
				_ => Error.Failed
			};

			if (err != Error.Ok || image.GetWidth() <= 0 || image.GetHeight() <= 0)
			{
				return null;
			}

			TryStoreInCas(bytes, extension, normalizedHash);
			ImageTexture texture = ImageTexture.CreateFromImage(image);
			TextureCache[normalizedHash] = texture;
			return texture;
		}
		catch
		{
			return null;
		}
	}

	private static void TryStoreInCas(byte[] bytes, string extension, string expectedNormalizedHash)
	{
		try
		{
			string computed = ContentAddressableStorage.NormalizeBlake3Hash(
				RealmMetadataHelper.ComputeBlake3(bytes, extension));
			if (string.Equals(computed, expectedNormalizedHash, StringComparison.OrdinalIgnoreCase))
			{
				MapAssetManager.Storage.StoreAsset(bytes, extension, precomputedBlake3: expectedNormalizedHash);
			}
		}
		catch
		{
		}
	}

	private static Texture2D? LoadTextureFromFile(string path)
	{
		try
		{
			var image = Image.LoadFromFile(path);
			if (image != null && image.GetWidth() > 0 && image.GetHeight() > 0)
			{
				return ImageTexture.CreateFromImage(image);
			}
		}
		catch
		{
		}

		return null;
	}

	private static string? DetectImageExtension(byte[] bytes)
	{
		if (bytes.Length >= 12 &&
		    bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
		    bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
		{
			return ".webp";
		}

		if (bytes.Length >= 8 &&
		    bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
		{
			return ".png";
		}

		if (bytes.Length >= 3 &&
		    bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
		{
			return ".jpg";
		}

		return null;
	}
}
