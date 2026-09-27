using System;
using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Realm.Shared.Textures;

public static class IndexedPngHelper
{
	public static void SaveAs256ColorPng(byte[] rgbaPixelBytes, int width, int height, string destinationPath)
	{
		string? directory = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		Marshal.Copy(rgbaPixelBytes, 0, bitmap.GetPixels(), rgbaPixelBytes.Length);
		using var skImage = SKImage.FromBitmap(bitmap);
		using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
		using var stream = File.Create(destinationPath);
		data.SaveTo(stream);
	}

	public static void SaveAs256ColorPng(SKBitmap image, string destinationPath)
	{
		string? directory = Path.GetDirectoryName(destinationPath);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		using var skImage = SKImage.FromBitmap(image);
		using var data = skImage.Encode(SKEncodedImageFormat.Png, 100);
		using var stream = File.Create(destinationPath);
		data.SaveTo(stream);
	}
}
