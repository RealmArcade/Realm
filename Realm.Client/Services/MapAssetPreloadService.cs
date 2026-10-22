using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Utils;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Realm.Shared.Textures;
using GodotImage = Godot.Image;

namespace Realm.Client.Services;

public class MapAssetPreloadService
{
	private enum PreloadAssetKind
	{
		None,
		Model,
		Texture
	}

	private readonly record struct PreloadEntry(string RelativePath, string FullPath, PreloadAssetKind Kind);

	private readonly List<PreloadEntry> _entries = new();
	private readonly List<string> _failedAssetPaths = new();
	private readonly List<Texture2D> _warmTextures = new();
	private int _nextIndex;

	public int TotalAssetCount => _entries.Count;

	public int ProcessedAssetCount => _nextIndex;

	public IReadOnlyList<string> FailedAssetPaths => _failedAssetPaths;

	public bool IsComplete => _nextIndex >= _entries.Count;

	public void Prepare(string mapDirectory)
	{
		_entries.Clear();
		_failedAssetPaths.Clear();
		_nextIndex = 0;

		if (string.IsNullOrEmpty(mapDirectory) || !Directory.Exists(mapDirectory)) return;

		string manifestPath = Path.Combine(mapDirectory, "manifest.json");
		if (!File.Exists(manifestPath)) return;

		MapManifest? manifest;
		try
		{
			manifest = MapFileService.LoadManifest(manifestPath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetPreloadService] Failed to read {manifestPath}: {ex.Message}");
			return;
		}

		if (manifest?.Files == null) return;

		foreach (string manifestKey in manifest.Files.Keys)
		{
			string relativePath = NormalizeRelativePath(manifestKey);
			PreloadAssetKind kind = ClassifyAsset(relativePath);
			if (kind == PreloadAssetKind.None) continue;

			string fullPath = Path.Combine(mapDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
			_entries.Add(new PreloadEntry(relativePath, fullPath, kind));
		}
	}

	public bool TryPreloadNext(out AssetIndexProgressUpdate progressUpdate)
	{
		if (_nextIndex >= _entries.Count)
		{
			progressUpdate = new AssetIndexProgressUpdate(1.0, TranslationServer.Translate("Assets ready."));
			return false;
		}

		PreloadEntry entry = _entries[_nextIndex];
		_nextIndex++;

		bool succeeded = entry.Kind switch
		{
			PreloadAssetKind.Model => TryWarmModel(entry),
			PreloadAssetKind.Texture => TryWarmTexture(entry),
			_ => true
		};

		if (!succeeded)
		{
			_failedAssetPaths.Add(entry.RelativePath);
		}

		double percentage = (double)_nextIndex / _entries.Count;
		string message = string.Format(
			TranslationServer.Translate("Preparing assets {0}/{1}: {2}"),
			_nextIndex,
			_entries.Count,
			Path.GetFileName(entry.RelativePath));

		progressUpdate = new AssetIndexProgressUpdate(percentage, message);
		return true;
	}

	public void ReleaseWarmTextures()
	{
		foreach (var texture in _warmTextures)
		{
			if (GodotObject.IsInstanceValid(texture))
			{
				texture.Dispose();
			}
		}

		_warmTextures.Clear();
	}

	private static string NormalizeRelativePath(string manifestKey)
	{
		string relativePath = manifestKey.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
			? manifestKey.Substring(6)
			: manifestKey;

		return relativePath.TrimStart('/', '\\').Replace('\\', '/');
	}

	private static PreloadAssetKind ClassifyAsset(string relativePath)
	{
		string extension = Path.GetExtension(relativePath);
		if (extension.Equals(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			return PreloadAssetKind.Model;
		}

		if (extension.Equals(".rtex", StringComparison.OrdinalIgnoreCase) ||
		    extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
		    extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
		    extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
		    extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
		{
			return PreloadAssetKind.Texture;
		}

		return PreloadAssetKind.None;
	}

	private static bool TryWarmModel(PreloadEntry entry)
	{
		if (!File.Exists(entry.FullPath)) return false;

		try
		{
			ModelCache.PreloadModel(entry.FullPath);
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetPreloadService] Failed to preload model {entry.RelativePath}: {ex.Message}");
			return false;
		}
	}

	private bool TryWarmTexture(PreloadEntry entry)
	{
		if (!File.Exists(entry.FullPath)) return false;

		try
		{
			Texture2D? texture = entry.RelativePath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase)
				? LoadRtexTexture(entry.FullPath)
				: LoadStandardTexture(entry.FullPath);

			if (texture == null) return false;

			_warmTextures.Add(texture);
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapAssetPreloadService] Failed to preload texture {entry.RelativePath}: {ex.Message}");
			return false;
		}
	}

	private static Texture2D? LoadRtexTexture(string fullPath)
	{
		byte[] rtexBytes = File.ReadAllBytes(fullPath);
		var (_, layers, _) = RtexFile.Parse(rtexBytes);
		if (layers.Count == 0 || layers[0] == null || layers[0].Length == 0) return null;

		var image = GodotImage.CreateEmpty(1, 1, false, GodotImage.Format.Rgba8);
		if (image.LoadWebpFromBuffer(layers[0]) != Error.Ok && image.LoadPngFromBuffer(layers[0]) != Error.Ok)
		{
			return null;
		}

		if (!image.HasMipmaps()) image.GenerateMipmaps();
		return ImageTexture.CreateFromImage(image);
	}

	private static Texture2D? LoadStandardTexture(string fullPath)
	{
		var image = GodotImage.LoadFromFile(fullPath);
		if (image == null) return null;

		if (!image.HasMipmaps()) image.GenerateMipmaps();
		return ImageTexture.CreateFromImage(image);
	}
}
