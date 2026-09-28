using System;
using System.Collections.Generic;
using System.IO;
using Imazen.WebP;
using SkiaSharp;

namespace Realm.Shared.Animation;

public static class AnimatedWebpEncoder
{
	public static void EncodeAnimatedWebp(List<SKBitmap> frames, string outputPath, float duration, bool lossless = false, int quality = 90)
	{
		if (frames == null || frames.Count == 0) return;

		int width = frames[0].Width;
		int height = frames[0].Height;
		int frameCount = frames.Count;
		float frameDurationSec = duration / frameCount;

		string? directory = Path.GetDirectoryName(outputPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		using var animEncoder = new AnimEncoder(width, height);

		for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
		{
			var frame = frames[frameIndex];
			byte[] pixelBytes;

			if (frame.ColorType == SKColorType.Rgba8888 && frame.AlphaType == SKAlphaType.Unpremul)
			{
				pixelBytes = frame.Bytes;
			}
			else
			{
				using var rgbaBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
				frame.CopyTo(rgbaBitmap, SKColorType.Rgba8888);
				pixelBytes = rgbaBitmap.Bytes;
			}

			int timestampMs = (int)MathF.Round((frameIndex + 1) * frameDurationSec * 1000.0f);
			var encoderConfig = new WebPEncoderConfig();
			if (lossless)
			{
				encoderConfig.SetLossless(true)
					.SetLosslessPreset(9)
					.SetMethod(6)
					.SetExact(true);
			}
			else
			{
				encoderConfig.SetQuality(Math.Clamp(quality, 0, 100))
					.SetMethod(6)
					.SetSharpYuv(true);
			}

			animEncoder.AddFrame(pixelBytes, width * 4, WebPPixelFormat.Rgba, timestampMs, encoderConfig);
		}

		byte[] webpBytes = animEncoder.Assemble();
		File.WriteAllBytes(outputPath, webpBytes);
	}
}
