using System.IO;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Realm.Shared.Textures;

public static class PngImageCodec
{
	public const int BytesPerPixel = 4;

	public static bool IsPng(ReadOnlySpan<byte> bytes)
	{
		return bytes.Length >= 8
		       && bytes[0] == 0x89
		       && bytes[1] == 0x50
		       && bytes[2] == 0x4E
		       && bytes[3] == 0x47
		       && bytes[4] == 0x0D
		       && bytes[5] == 0x0A
		       && bytes[6] == 0x1A
		       && bytes[7] == 0x0A;
	}

	public static byte[] DecodeRgba8(ReadOnlySpan<byte> bytes, out int width, out int height)
	{
		using var stream = new SKMemoryStream(bytes.ToArray());
		using SKCodec? codec = SKCodec.Create(stream);
		if (codec == null)
		{
			throw new InvalidDataException($"A PNG of {bytes.Length} bytes did not decode.");
		}

		width = codec.Info.Width;
		height = codec.Info.Height;

		var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		byte[] pixels = new byte[width * height * BytesPerPixel];
		SKCodecResult result = DecodeInto(codec, info, pixels, width, height);
		if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
		{
			throw new InvalidDataException($"A {width}x{height} PNG did not decode: {result}.");
		}

		return pixels;
	}

	public static byte[] DecodeRgba8(string path, out int width, out int height)
	{
		byte[] bytes = File.ReadAllBytes(path);
		try
		{
			return DecodeRgba8(bytes, out width, out height);
		}
		catch (InvalidDataException exception)
		{
			throw new InvalidDataException($"The PNG at '{path}' did not decode.", exception);
		}
	}

	public static byte[] EncodeRgba8(ReadOnlySpan<byte> rgba, int width, int height)
	{
		using var bitmap = ToBitmap(rgba, width, height);
		using SKImage image = SKImage.FromBitmap(bitmap);
		using SKData encoded = image.Encode(SKEncodedImageFormat.Png, 100);
		return encoded.ToArray();
	}

	public static void EncodeRgba8(string path, ReadOnlySpan<byte> rgba, int width, int height)
	{
		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

		File.WriteAllBytes(path, EncodeRgba8(rgba, width, height));
	}

	private static unsafe SKCodecResult DecodeInto(SKCodec codec, SKImageInfo info, byte[] pixels, int width, int height)
	{
		int expectedBytes = width * height * BytesPerPixel;
		if (pixels.Length != expectedBytes)
		{
			throw new ArgumentException($"A {width}x{height} image needs {expectedBytes} bytes but was handed {pixels.Length}.");
		}

		fixed (byte* destination = pixels)
		{
			return codec.GetPixels(info, (IntPtr)destination);
		}
	}

	private static SKBitmap ToBitmap(ReadOnlySpan<byte> rgba, int width, int height)
	{
		int expectedBytes = width * height * BytesPerPixel;
		if (rgba.Length != expectedBytes)
		{
			throw new ArgumentException($"A {width}x{height} image needs {expectedBytes} bytes but was handed {rgba.Length}.");
		}

		var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
		Marshal.Copy(rgba.ToArray(), 0, bitmap.GetPixels(), expectedBytes);
		return bitmap;
	}
}
