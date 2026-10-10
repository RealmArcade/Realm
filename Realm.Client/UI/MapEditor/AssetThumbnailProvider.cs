using Godot;
using Realm.Client.Services;
using Realm.Shared.Textures;
using System;
using System.Collections.Generic;
using System.IO;

namespace Realm.Client.UI.MapEditor;

public static class AssetThumbnailProvider
{
	public static event Action<string, Texture2D>? ThumbnailGenerated;

	private static readonly Dictionary<string, Texture2D> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, AnimatedThumbnail> _animatedThumbnailCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, Texture2D> _formatBadgeCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly List<string> _lruOrder = new();
	private const int MaxCacheEntries = 400;

	private static string? _rtexCacheDir;
	private static string? _ranimCacheDir;
	private static string? _modelCacheDir;
	private static string? _imageCacheDir;

	public static string RtexCacheDir => _rtexCacheDir ??= ProjectSettings.GlobalizePath("user://rtex_thumb_cache");
	public static string RanimCacheDir => _ranimCacheDir ??= ProjectSettings.GlobalizePath("user://ranim_thumb_cache");
	public static string ModelCacheDir => _modelCacheDir ??= ProjectSettings.GlobalizePath("user://model_thumb_cache");
	public static string ImageCacheDir => _imageCacheDir ??= ProjectSettings.GlobalizePath("user://image_thumb_cache");

	public static string NormalizePath(string path)
	{
		if (string.IsNullOrEmpty(path)) return string.Empty;
		return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
	}

	static AssetThumbnailProvider()
	{
		GlbThumbnailRenderer.ThumbnailGenerated += OnGlbThumbnailGenerated;
	}

	private static void OnGlbThumbnailGenerated(string filePath, Texture2D texture)
	{
		string normPath = NormalizePath(filePath);
		lock (_thumbnailCache)
		{
			_thumbnailCache[normPath] = texture;
			TouchLru(normPath);
		}
		ThumbnailGenerated?.Invoke(normPath, texture);
	}

	public static Texture2D? GetThumbnail(IndexedAsset asset, bool isHighPriority = true)
	{
		if (asset == null || string.IsNullOrEmpty(asset.FilePath))
			return GetPlaceholderTexture("?");

		string normPath = NormalizePath(asset.FilePath);
		string ext = asset.Extension.ToLowerInvariant();
        
		if (ext == ".ranim")
			return HandleRanimThumbnail(asset);

		lock (_thumbnailCache)
		{
			if (_thumbnailCache.TryGetValue(normPath, out var cachedTexture) && cachedTexture != null)
			{
				TouchLru(normPath);
				return cachedTexture;
			}
		}

		Texture2D? generatedTexture = GenerateThumbnailDirect(asset, isHighPriority);
		if (generatedTexture != null)
		{
			CacheThumbnail(normPath, generatedTexture);
			return generatedTexture;
		}

		return GetPlaceholderTexture(asset.Extension.TrimStart('.').ToUpperInvariant());
	}

	private static void CacheThumbnail(string normPath, Texture2D texture)
	{
		lock (_thumbnailCache)
		{
			if (_thumbnailCache.Count >= MaxCacheEntries && _lruOrder.Count > 0)
			{
				string oldestKey = _lruOrder[0];
				_lruOrder.RemoveAt(0);
				_thumbnailCache.Remove(oldestKey);
			}

			_thumbnailCache[normPath] = texture;
			TouchLru(normPath);
		}
	}

	private static Texture2D? HandleRanimThumbnail(IndexedAsset asset)
	{
		var animThumb = GetAnimatedThumbnail(asset);
		if (animThumb?.PrimaryFrame != null)
			return animThumb.PrimaryFrame;
		return GetPlaceholderTexture("RANIM");
	}

	public static AnimatedThumbnail? GetAnimatedThumbnail(IndexedAsset asset)
	{
		if (asset == null || string.IsNullOrEmpty(asset.FilePath)) return null;
		return GetAnimatedThumbnail(asset.FilePath, asset.Blake3);
	}

	public static AnimatedThumbnail? GetAnimatedThumbnail(string filePath, string? blake3 = null)
	{
		if (string.IsNullOrEmpty(filePath)) return null;

		string normPath = NormalizePath(filePath);
		lock (_animatedThumbnailCache)
		{
			if (_animatedThumbnailCache.TryGetValue(normPath, out var cachedAnim) && cachedAnim != null)
				return cachedAnim;
		}

		if (!File.Exists(normPath)) return null;

		string cachedPngPath = GetDiskCachePath(normPath, blake3);
		Texture2D? diskPrimaryFrame = LoadImageTextureFromDisk(cachedPngPath);

		var generatedAnim = TryGenerateAndCacheAnimatedThumbnail(normPath, cachedPngPath, diskPrimaryFrame);
		if (generatedAnim != null) return generatedAnim;

		if (diskPrimaryFrame != null)
		{
			return new AnimatedThumbnail
			{
				Frames = new List<Texture2D> { diskPrimaryFrame },
				Fps = 1.0f
			};
		}

		return null;
	}

	private static AnimatedThumbnail? TryGenerateAndCacheAnimatedThumbnail(string normPath, string cachedPngPath, Texture2D? diskPrimaryFrame)
	{
		try
		{
			var animThumb = RanimSkeletonThumbnailGenerator.GenerateAnimatedThumbnail(normPath);
			if (animThumb == null || animThumb.Frames.Count == 0) return null;

			lock (_animatedThumbnailCache)
			{
				if (_animatedThumbnailCache.Count >= MaxCacheEntries)
					_animatedThumbnailCache.Clear();
				_animatedThumbnailCache[normPath] = animThumb;
			}

			if (diskPrimaryFrame == null && animThumb.PrimaryFrame != null)
			{
				var pImg = animThumb.PrimaryFrame.GetImage();
				if (pImg != null && !pImg.IsEmpty())
					SaveThumbnailAtomic(pImg, cachedPngPath);
			}

			return animThumb;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetThumbnailProvider] Error loading ranim {normPath}: {ex.Message}");
			return null;
		}
	}

	private static Texture2D? LoadImageTextureFromDisk(string path)
	{
		if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
		try
		{
			var cachedImg = Image.LoadFromFile(path);
			if (cachedImg != null && !cachedImg.IsEmpty())
				return ImageTexture.CreateFromImage(cachedImg);
		}
		catch { }
		return null;
	}

	private static void TouchLru(string key)
	{
		_lruOrder.Remove(key);
		_lruOrder.Add(key);
	}

	private static Texture2D? GenerateThumbnailDirect(IndexedAsset asset, bool isHighPriority = true)
	{
		string ext = asset.Extension.ToLowerInvariant();
		return ext switch
		{
			".rtex" => LoadRtexAlbedoThumbnail(asset.FilePath, asset.LastModifiedUtc, asset.Blake3, isHighPriority),
			".rmesh" or ".glb" or ".gltf" => LoadGlbThumbnail(asset.FilePath, asset.LastModifiedUtc, asset.Blake3, isHighPriority),
			".raud" or ".ogg" or ".wav" or ".mp3" => LoadAudioThumbnail(ext),
			".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tga" => LoadRasterImageThumbnail(asset.FilePath, asset.LastModifiedUtc, asset.Blake3, isHighPriority),
			".svg" => LoadSvgThumbnail(asset.FilePath, asset.LastModifiedUtc, asset.Blake3, isHighPriority),
			_ => GetPlaceholderTexture(ext.TrimStart('.').ToUpperInvariant())
		};
	}

	public static bool IsImageExtension(string extension)
	{
		if (string.IsNullOrEmpty(extension)) return false;
		string ext = extension.ToLowerInvariant();
		if (!ext.StartsWith(".")) ext = "." + ext;
		return ext is ".rtex" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".tga" or ".svg";
	}

	public static string GetDiskCachePath(string filePath, string? blake3 = null)
	{
		string normPath = NormalizePath(filePath);
		string ext = Path.GetExtension(normPath).ToLowerInvariant();
		string cacheDir = ext switch
		{
			".rtex" => RtexCacheDir,
			".ranim" => RanimCacheDir,
			".rmesh" or ".glb" or ".gltf" => ModelCacheDir,
			_ => ImageCacheDir
		};
		string hash = GlbThumbnailRenderer.GetBlake3(normPath, blake3);
		if (string.IsNullOrEmpty(hash) || hash.Length < 2)
		{
			return string.Empty;
		}
		return Path.Combine(cacheDir, hash.Substring(0, 2), $"{hash}.png");
	}

	public static string GetDiskCachePath(string filePath, DateTime lastModifiedUtc, string? blake3 = null)
	{
		return GetDiskCachePath(filePath, blake3);
	}

	public static void SaveThumbnailAtomic(Image img, string cachedPngPath)
	{
		if (string.IsNullOrEmpty(cachedPngPath) || img == null || img.IsEmpty()) return;
		try
		{
			string? dir = Path.GetDirectoryName(cachedPngPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			string tempPath = cachedPngPath + $".tmp_{Guid.NewGuid():N}";
			IndexedPngHelper.SaveAs256ColorPng(
				img.GetData(),
				img.GetWidth(),
				img.GetHeight(),
				tempPath);

			if (File.Exists(tempPath))
			{
				try
				{
					File.Move(tempPath, cachedPngPath, overwrite: true);
				}
				catch
				{
					if (File.Exists(tempPath))
					{
						File.Delete(tempPath);
					}
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetThumbnailProvider] SaveThumbnailAtomic error on {cachedPngPath}: {ex.Message}");
		}
	}

	private class ImageThumbnailRequest
	{
		public string FilePath { get; set; } = string.Empty;
		public DateTime LastModifiedUtc { get; set; }
		public string? Blake3 { get; set; }
		public bool IsHighPriority { get; set; }
	}

	private static readonly LinkedList<ImageThumbnailRequest> _imageRequestQueue = new();
	private static readonly HashSet<string> _pendingImagePaths = new(StringComparer.OrdinalIgnoreCase);
	private static readonly object _imageQueueLock = new();
	private static bool _isImageWorkerRunning = false;

	public static void EnqueueImageRequest(string filePath, DateTime lastModifiedUtc, string? blake3 = null, bool isHighPriority = true)
	{
		string normPath = NormalizePath(filePath);
		if (string.IsNullOrEmpty(normPath)) return;

		lock (_imageQueueLock)
		{
			if (_pendingImagePaths.Contains(normPath))
			{
				return;
			}
			_pendingImagePaths.Add(normPath);

			var req = new ImageThumbnailRequest
			{
				FilePath = normPath,
				LastModifiedUtc = lastModifiedUtc,
				Blake3 = blake3,
				IsHighPriority = isHighPriority
			};

			if (isHighPriority)
			{
				_imageRequestQueue.AddFirst(req);
			}
			else
			{
				_imageRequestQueue.AddLast(req);
			}

			if (!_isImageWorkerRunning)
			{
				_isImageWorkerRunning = true;
				System.Threading.Tasks.Task.Run(ProcessImageQueueAsync);
			}
		}
	}

	private static void ProcessImageQueueAsync()
	{
		while (true)
		{
			ImageThumbnailRequest? currentRequest = null;
			lock (_imageQueueLock)
			{
				if (_imageRequestQueue.Count > 0)
				{
					currentRequest = _imageRequestQueue.First.Value;
					_imageRequestQueue.RemoveFirst();
				}
				else
				{
					_isImageWorkerRunning = false;
					break;
				}
			}

			if (currentRequest != null)
			{
				ProcessSingleImageRequest(currentRequest);
			}
		}
	}

	private static void ProcessSingleImageRequest(ImageThumbnailRequest req)
	{
		string reqPath = req.FilePath;
		string? reqBlake = req.Blake3;
		try
		{
			GenerateDiskThumbnail(reqPath, req.LastModifiedUtc, reqBlake);

			if (Engine.GetMainLoop() is SceneTree)
			{
				Callable.From(() => DispatchThumbnailGeneratedDeferred(reqPath, reqBlake)).CallDeferred();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetThumbnailProvider] Error in ProcessImageQueueAsync for {reqPath}: {ex.Message}");
		}
		finally
		{
			lock (_imageQueueLock)
			{
				_pendingImagePaths.Remove(reqPath);
			}
		}
	}

	private static void DispatchThumbnailGeneratedDeferred(string reqPath, string? reqBlake)
	{
		var tex = LoadThumbnailFromDisk(reqPath, reqBlake);
		if (tex == null) return;
		
		CacheThumbnail(reqPath, tex);
		ThumbnailGenerated?.Invoke(reqPath, tex);
	}

	public static void NotifyThumbnailGeneratedFromDiskDeferred(string normPath)
	{
		if (Engine.GetMainLoop() is SceneTree)
		{
			Callable.From(() => DispatchThumbnailGeneratedDeferred(normPath, null)).CallDeferred();
		}
	}

	public static Texture2D? LoadThumbnailFromDisk(string filePath, string? blake3 = null)
	{
		string normPath = NormalizePath(filePath);
		if (string.IsNullOrEmpty(normPath)) return null;

		string ext = Path.GetExtension(normPath).ToLowerInvariant();
		if (ext == ".rmesh" || ext == ".glb" || ext == ".gltf")
		{
			if (GlbThumbnailRenderer.TryGetDiskCached(normPath, blake3, out var rmeshTex))
				return rmeshTex;
			return null;
		}

		string cachedPngPath = GetDiskCachePath(normPath, blake3);
		return LoadImageTextureFromDisk(cachedPngPath);
	}

	public static void EnsureDiskImageThumbnail(string filePath, DateTime lastModifiedUtc, string? blake3 = null)
	{
		GenerateDiskThumbnail(filePath, lastModifiedUtc, blake3);
	}

	public static void GenerateDiskThumbnail(string filePath, DateTime lastModifiedUtc, string? blake3 = null)
	{
		string normPath = NormalizePath(filePath);
		if (string.IsNullOrEmpty(normPath) || !File.Exists(normPath)) return;

		string ext = Path.GetExtension(normPath).ToLowerInvariant();
		if (!IsImageExtension(ext) && ext != ".ranim") return;

		string cachedPngPath = GetDiskCachePath(normPath, blake3);
		if (string.IsNullOrEmpty(cachedPngPath) || File.Exists(cachedPngPath)) return;

		try
		{
			TryGenerateDiskThumbnailInternal(normPath, ext, cachedPngPath, blake3);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[AssetThumbnailProvider] GenerateDiskThumbnail error on {normPath}: {ex.Message}");
		}
	}

	private static void TryGenerateDiskThumbnailInternal(string normPath, string ext, string cachedPngPath, string? blake3)
	{
		if (ext == ".ranim")
		{
			RanimSkeletonThumbnailGenerator.EnsureDiskRanimThumbnail(normPath, blake3);
			return;
		}

		Image? img = LoadImageForThumbnail(normPath, ext);
		if (img == null || img.IsEmpty()) return;

		ProcessAndSaveThumbnailImage(img, cachedPngPath);
	}

	private static void ProcessAndSaveThumbnailImage(Image img, string cachedPngPath)
	{
		if (img.GetFormat() != Image.Format.Rgba8)
			img.Convert(Image.Format.Rgba8);

		if (img.GetWidth() > 128 || img.GetHeight() > 128)
			img.Resize(128, 128, Image.Interpolation.Bilinear);

		SaveThumbnailAtomic(img, cachedPngPath);
	}

	private static Image? LoadImageForThumbnail(string normPath, string ext)
	{
		if (ext == ".rtex")
			return LoadRtexImage(normPath);
		
		if (ext == ".svg")
		{
			var svgImg = new Image();
			if (svgImg.Load(normPath) == Error.Ok) return svgImg;
			return null;
		}

		return Image.LoadFromFile(normPath);
	}

	private static Image? LoadRtexImage(string normPath)
	{
		byte[]? layer0Bytes = GetRtexLayerBytes(normPath);
		if (layer0Bytes == null || layer0Bytes.Length == 0) return null;

		return DecodeImageFromBuffer(layer0Bytes);
	}

	private static byte[]? GetRtexLayerBytes(string normPath)
	{
		byte[]? layer0Bytes = Realm.Shared.Textures.RtexFile.GetLayerFromFile(normPath, 0);
		if (layer0Bytes != null && layer0Bytes.Length > 0)
			return layer0Bytes;

		byte[] bytes = File.ReadAllBytes(normPath);
		if (Realm.Shared.Textures.RtexFile.IsRtexBytes(bytes))
			return Realm.Shared.Textures.RtexFile.GetLayer(bytes, 0);

		return bytes;
	}

	private static Image? DecodeImageFromBuffer(byte[] buffer)
	{
		var decodedImg = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
		
		if (decodedImg.LoadWebpFromBuffer(buffer) == Error.Ok) return decodedImg;
		if (decodedImg.LoadPngFromBuffer(buffer) == Error.Ok) return decodedImg;
		if (decodedImg.LoadJpgFromBuffer(buffer) == Error.Ok) return decodedImg;
		if (decodedImg.LoadTgaFromBuffer(buffer) == Error.Ok) return decodedImg;
		if (decodedImg.LoadBmpFromBuffer(buffer) == Error.Ok) return decodedImg;

		return null;
	}

	private static Texture2D? LoadGlbThumbnail(string glbPath, DateTime lastModifiedUtc, string? blake3 = null, bool isHighPriority = true)
	{
		string normPath = NormalizePath(glbPath);
		if (GlbThumbnailRenderer.TryGetDiskCached(normPath, blake3, out var cachedTexture))
		{
			return cachedTexture;
		}

		GlbThumbnailRenderer.EnqueueRequest(normPath, lastModifiedUtc, blake3, isHighPriority: isHighPriority);
		return null;
	}

	private static Texture2D? LoadRtexAlbedoThumbnail(string rtexPath, DateTime lastModifiedUtc, string? blake3 = null, bool isHighPriority = true)
	{
		string normPath = NormalizePath(rtexPath);
		if (string.IsNullOrEmpty(normPath) || !File.Exists(normPath)) return null;

		string cachedPngPath = GetDiskCachePath(normPath, blake3);
		var diskTexture = LoadImageTextureFromDisk(cachedPngPath);
		if (diskTexture != null) return diskTexture;

		EnqueueImageRequest(normPath, lastModifiedUtc, blake3, isHighPriority);
		return null;
	}

	private static Texture2D? LoadRasterImageThumbnail(string imagePath, DateTime lastModifiedUtc, string? blake3 = null, bool isHighPriority = true)
	{
		string normPath = NormalizePath(imagePath);
		if (string.IsNullOrEmpty(normPath) || !File.Exists(normPath)) return null;

		string cachedPngPath = GetDiskCachePath(normPath, blake3);
		var diskTexture = LoadImageTextureFromDisk(cachedPngPath);
		if (diskTexture != null) return diskTexture;

		EnqueueImageRequest(normPath, lastModifiedUtc, blake3, isHighPriority);
		return null;
	}

	private static Texture2D? LoadSvgThumbnail(string svgPath, DateTime lastModifiedUtc, string? blake3 = null, bool isHighPriority = true)
	{
		string normPath = NormalizePath(svgPath);
		if (string.IsNullOrEmpty(normPath) || !File.Exists(normPath)) return null;

		string cachedPngPath = GetDiskCachePath(normPath, blake3);
		var diskTexture = LoadImageTextureFromDisk(cachedPngPath);
		if (diskTexture != null) return diskTexture;

		EnqueueImageRequest(normPath, lastModifiedUtc, blake3, isHighPriority);
		return null;
	}

	public static Texture2D LoadAudioThumbnail(string extension)
	{
		string extUpper = extension.TrimStart('.').ToUpperInvariant();
		string cacheKey = $"AUDIO_{extUpper}";

		lock (_formatBadgeCache)
		{
			if (_formatBadgeCache.TryGetValue(cacheKey, out var cached))
				return cached;

			var texture = GenerateAudioThumbnailTexture();
			_formatBadgeCache[cacheKey] = texture;
			return texture;
		}
	}

	private static Texture2D GenerateAudioThumbnailTexture()
	{
		var img = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
		DrawAudioThumbnailBackground(img);
		DrawAudioThumbnailPlayIcon(img);
		DrawAudioThumbnailBars(img);
		return ImageTexture.CreateFromImage(img);
	}

	private static void DrawAudioThumbnailBackground(Image img)
	{
		var bgTop = new Color(0.06f, 0.14f, 0.10f, 1.0f);
		var bgBottom = new Color(0.10f, 0.22f, 0.16f, 1.0f);
		var borderColor = new Color(0.18f, 0.45f, 0.30f, 0.9f);
		for (int y = 0; y < 128; y++)
		{
			float t = (float)y / 128f;
			var rowColor = bgTop.Lerp(bgBottom, t);
			for (int x = 0; x < 128; x++)
			{
				bool isBorder = (x == 0 || x == 127 || y == 0 || y == 127);
				img.SetPixel(x, y, isBorder ? borderColor : rowColor);
			}
		}
	}

	private static void DrawAudioThumbnailPlayIcon(Image img)
	{
		var playColor = new Color(0.96f, 0.82f, 0.38f, 1.0f);
		var shadowColor = new Color(0.02f, 0.04f, 0.03f, 0.8f);
		DrawPlayTriangle(img, 46, 34, 46, 86, 86, 60, shadowColor, 2);
		DrawPlayTriangle(img, 44, 32, 44, 84, 84, 58, playColor, 0);
	}

	private static void DrawAudioThumbnailBars(Image img)
	{
		int[] barHeights = new[] { 10, 18, 14, 24, 16, 20, 12 };
		int startX = 31;
		var barColor = new Color(0.20f, 0.70f, 0.45f, 1.0f);
		var capColor = new Color(0.96f, 0.82f, 0.38f, 1.0f);

		for (int b = 0; b < barHeights.Length; b++)
		{
			int bx = startX + b * 10;
			int bh = barHeights[b];
			int by = 112 - bh;
			for (int py = by; py <= 112; py++)
			{
				var col = (py <= by + 2) ? capColor : barColor;
				for (int px = bx; px < bx + 6; px++)
				{
					if (px < 127 && py < 127) img.SetPixel(px, py, col);
				}
			}
		}
	}

	private static void DrawPlayTriangle(Image img, int x0, int y0, int x1, int y1, int x2, int y2, Color color, int offset)
	{
		x0 += offset; y0 += offset;
		x1 += offset; y1 += offset;
		x2 += offset; y2 += offset;

		int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
		int maxX = Math.Min(img.GetWidth() - 1, Math.Max(x0, Math.Max(x1, x2)));
		int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
		int maxY = Math.Min(img.GetHeight() - 1, Math.Max(y0, Math.Max(y1, y2)));

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				if (PointInTriangle(x, y, x0, y0, x1, y1, x2, y2))
				{
					img.SetPixel(x, y, color);
				}
			}
		}
	}

	private static bool PointInTriangle(int px, int py, int x0, int y0, int x1, int y1, int x2, int y2)
	{
		float d1 = Sign(px, py, x0, y0, x1, y1);
		float d2 = Sign(px, py, x1, y1, x2, y2);
		float d3 = Sign(px, py, x2, y2, x0, y0);

		bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
		bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

		return !(hasNeg && hasPos);
	}

	private static float Sign(int px, int py, int x1, int y1, int x2, int y2)
	{
		return (px - x2) * (y1 - y2) - (x1 - x2) * (py - y2);
	}

	public static Texture2D GetPlaceholderTexture(string formatBadge)
	{
		string key = string.IsNullOrEmpty(formatBadge) ? "FILE" : formatBadge.ToUpperInvariant();

		lock (_formatBadgeCache)
		{
			if (_formatBadgeCache.TryGetValue(key, out var cachedBadge))
				return cachedBadge;

			var texture = GeneratePlaceholderTexture(key);
			_formatBadgeCache[key] = texture;
			return texture;
		}
	}

	private static Texture2D GeneratePlaceholderTexture(string key)
	{
		var image = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8);
		Color baseBackgroundColor = GetPlaceholderColor(key);
		Color borderColor = new Color(baseBackgroundColor.R * 1.5f, baseBackgroundColor.G * 1.5f, baseBackgroundColor.B * 1.5f, 0.8f);

		FillPlaceholderImage(image, baseBackgroundColor, borderColor);

		return ImageTexture.CreateFromImage(image);
	}

	private static Color GetPlaceholderColor(string key)
	{
		if (IsModelFormat(key)) return new Color(0.18f, 0.28f, 0.42f, 1.0f);
		if (IsAnimFormat(key)) return new Color(0.38f, 0.22f, 0.45f, 1.0f);
		if (IsAudioFormat(key)) return new Color(0.20f, 0.42f, 0.30f, 1.0f);
		if (IsImageFormat(key)) return new Color(0.35f, 0.32f, 0.18f, 1.0f);
		if (IsTextFormat(key)) return new Color(0.28f, 0.28f, 0.30f, 1.0f);
		
		return new Color(0.20f, 0.22f, 0.26f, 1.0f);
	}

	private static bool IsModelFormat(string key)
	{
		return key == "GLB" || key == "GLTF" || key == "FBX";
	}

	private static bool IsAnimFormat(string key)
	{
		return key == "RANIM" || key == "ANIM";
	}

	private static bool IsAudioFormat(string key)
	{
		return key == "OGG" || key == "WAV" || key == "MP3";
	}

	private static bool IsImageFormat(string key)
	{
		return key == "RTEX" || key == "KTX2" || key == "PNG" || key == "JPG" || key == "JPEG" || key == "WEBP";
	}

	private static bool IsTextFormat(string key)
	{
		return key == "JSON" || key == "TXT";
	}

	private static void FillPlaceholderImage(Image image, Color baseColor, Color borderColor)
	{
		for (int y = 0; y < 128; y++)
		{
			for (int x = 0; x < 128; x++)
			{
				bool isBorder = IsBorderPixel(x, y);
				image.SetPixel(x, y, isBorder ? borderColor : baseColor);
			}
		}
	}

	private static bool IsBorderPixel(int x, int y)
	{
		return x < 2 || x >= 126 || y < 2 || y >= 126;
	}
}