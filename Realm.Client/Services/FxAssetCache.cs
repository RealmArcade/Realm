using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Shared.Textures;

namespace Realm.Client.Services;

/// <summary>
///     Loading an effect's texture once: the workspace-relative path a texture name resolves to, the
///     loaded image (or its .rtex layer) behind it, and the sprite frames sliced out of an atlas, each
///     cached against the key that produced it.
/// </summary>
public sealed class FxAssetCache
{
	private readonly Dictionary<string, string> _resolvedPathCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Texture2D?> _textureCache = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, (SpriteFrames Frames, int FrameWidth)> _spriteFramesCache = new(StringComparer.OrdinalIgnoreCase);

	public (SpriteFrames Frames, int FrameWidth)? TryGetSpritesheetFrames(string texturePath, int columns, int rows, float framesPerSecond, bool loop)
	{
		if (string.IsNullOrWhiteSpace(texturePath)) return null;

		int safeColumns = Math.Max(1, columns);
		int safeRows = Math.Max(1, rows);
		float safeFramesPerSecond = framesPerSecond > 0.0001f ? framesPerSecond : 12.0f;

		string fullPath = ResolveTexturePathCached(texturePath);
		string framesKey = $"{fullPath}|{safeColumns}|{safeRows}|{safeFramesPerSecond:F4}|{loop}";

		if (_spriteFramesCache.TryGetValue(framesKey, out var cachedFrames))
		{
			return cachedFrames;
		}

		Texture2D? texture = LoadTextureCached(fullPath);
		if (texture == null) return null;

		int totalFrames = safeColumns * safeRows;
		var frames = new SpriteFrames();
		frames.AddAnimation("play");
		frames.SetAnimationLoopMode("play", loop ? SpriteFrames.LoopMode.Linear : SpriteFrames.LoopMode.None);
		frames.SetAnimationSpeed("play", safeFramesPerSecond);

		int frameWidth = Math.Max(1, texture.GetWidth() / safeColumns);
		int frameHeight = Math.Max(1, texture.GetHeight() / safeRows);

		for (int frameIndex = 0; frameIndex < totalFrames; frameIndex++)
		{
			int col = frameIndex % safeColumns;
			int row = frameIndex / safeColumns;
			var atlasFrame = new AtlasTexture
			{
				Atlas = texture,
				Region = new Rect2(col * frameWidth, row * frameHeight, frameWidth, frameHeight)
			};
			frames.AddFrame("play", atlasFrame);
		}

		cachedFrames = (frames, frameWidth);
		_spriteFramesCache[framesKey] = cachedFrames;
		return cachedFrames;
	}

	string ResolveTexturePathCached(string texturePath)
	{
		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string key = $"{wsPath}|{texturePath}";
		if (_resolvedPathCache.TryGetValue(key, out var cached))
		{
			return cached;
		}

		string resolved = ResolveTexturePath(texturePath, wsPath);
		_resolvedPathCache[key] = resolved;
		return resolved;
	}

	static string ResolveTexturePath(string texturePath, string wsPath)
	{
		if (texturePath.StartsWith("res://") || File.Exists(texturePath))
			return texturePath;

		string fullPath = Path.Combine(wsPath, texturePath.Replace('/', Path.DirectorySeparatorChar));
		if (File.Exists(fullPath)) return fullPath;

		fullPath = Path.Combine(wsPath, "Assets", "vfx", Path.GetFileName(texturePath));
		if (File.Exists(fullPath)) return fullPath;

		string rtexCandidate = Path.ChangeExtension(fullPath, ".rtex");
		if (File.Exists(rtexCandidate)) return rtexCandidate;

		return fullPath;
	}

	Texture2D? LoadTextureCached(string fullPath)
	{
		if (_textureCache.TryGetValue(fullPath, out var cached) && (cached == null || GodotObject.IsInstanceValid(cached)))
		{
			return cached;
		}

		var loaded = LoadTexture(fullPath);
		_textureCache[fullPath] = loaded;
		return loaded;
	}

	static Texture2D? LoadTexture(string fullPath)
	{
		if (fullPath.StartsWith("res://"))
		{
			return GD.Load<Texture2D>(fullPath);
		}

		if (!File.Exists(fullPath))
			return null;

		Image? img = null;
		if (fullPath.EndsWith(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			img = LoadRtexImage(fullPath);
		}
		else
		{
			img = Image.LoadFromFile(fullPath);
		}

		if (img == null) return null;

		if (!img.HasMipmaps())
		{
			img.GenerateMipmaps();
		}

		return ImageTexture.CreateFromImage(img);
	}

	static Image? LoadRtexImage(string fullPath)
	{
		byte[] rtexBytes = File.ReadAllBytes(fullPath);
		byte[]? webpBytes = RtexFile.GetLayer(rtexBytes, 0);
		if (webpBytes == null || webpBytes.Length == 0) return null;

		var img = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		if (img.LoadWebpFromBuffer(webpBytes) != Error.Ok)
		{
			img.LoadPngFromBuffer(webpBytes);
		}

		return img;
	}
}
