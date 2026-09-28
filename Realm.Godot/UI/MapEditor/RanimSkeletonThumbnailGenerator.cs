using Godot;
using Realm.Shared.Animation;
using System.Collections.Generic;

public static class RanimSkeletonThumbnailGenerator
{
	public static void EnsureDiskRanimThumbnail(string filePath, string? blake3 = null)
	{
		if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

		string cachedPngPath = AssetThumbnailProvider.GetDiskCachePath(filePath, blake3);
		if (string.IsNullOrEmpty(cachedPngPath) || System.IO.File.Exists(cachedPngPath)) return;

		var animData = RealmAnimationSerializer.LoadFromFile(filePath);
		if (animData == null) return;

		var options = new RanimRenderOptions
		{
			Width = 128,
			Height = 128,
			Fps = 6.0f,
			MaxFrameCount = 1,
			Format = RanimOutputFormat.Webp,
			Scale = 1.0f,
			DrawBorder = true,
			DrawShadow = true
		};

		var renderResult = RanimRenderer.RenderFrames(animData, options);
		if (renderResult == null || renderResult.Frames.Count == 0) return;

		var frame0 = renderResult.Frames[0];
		var godotImage = Image.CreateFromData(frame0.Width, frame0.Height, false, Image.Format.Rgba8, frame0.RgbaBytes);
		AssetThumbnailProvider.SaveThumbnailAtomic(godotImage, cachedPngPath);
	}

	public static AnimatedThumbnail? GenerateAnimatedThumbnail(string filePath)
	{
		if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
		{
			return null;
		}

		var animData = RealmAnimationSerializer.LoadFromFile(filePath);
		return GenerateAnimatedThumbnail(animData);
	}

	public static AnimatedThumbnail? GenerateAnimatedThumbnail(RealmAnimationData animData)
	{
		if (animData == null)
		{
			return null;
		}

		if (System.Threading.Thread.CurrentThread.ManagedThreadId != 1)
		{
			return null;
		}

		var options = new RanimRenderOptions
		{
			Width = 128,
			Height = 128,
			Fps = 6.0f,
			MaxFrameCount = 12,
			Format = RanimOutputFormat.Webp,
			Scale = 1.0f,
			DrawBorder = true,
			DrawShadow = true
		};

		var renderResult = RanimRenderer.RenderFrames(animData, options);
		if (renderResult == null || renderResult.Frames.Count == 0)
		{
			return null;
		}

		var frames = new List<Texture2D>();
		foreach (var frame in renderResult.Frames)
		{
			var godotImage = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, frame.RgbaBytes);
			var texture = ImageTexture.CreateFromImage(godotImage);
			frames.Add(texture);
		}

		return new AnimatedThumbnail
		{
			Frames = frames,
			Fps = renderResult.EffectiveFps
		};
	}
}
