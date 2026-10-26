using System.IO;
using TinyEXR;

namespace Realm.Shared.Textures;

public static class ExrImageCodec
{
	public const int ChannelsPerPixel = 4;

	public static bool IsExr(ReadOnlySpan<byte> bytes) => Exr.IsEXRFromMemory(bytes);

	public static float[] DecodeRgbaFloat(ReadOnlySpan<byte> bytes, out int width, out int height)
	{
		ResultCode result = Exr.LoadEXRFromMemory(bytes, out float[] rgba, out int decodedWidth, out int decodedHeight);
		if (result != ResultCode.Success)
		{
			throw new InvalidDataException($"An EXR of {bytes.Length} bytes did not decode: {result}.");
		}

		width = decodedWidth;
		height = decodedHeight;
		return rgba;
	}

	public static float[] DecodeRgbaFloat(string path, out int width, out int height)
	{
		ResultCode result = Exr.LoadEXR(path, out float[] rgba, out int decodedWidth, out int decodedHeight);
		if (result != ResultCode.Success)
		{
			throw new InvalidDataException($"The EXR at '{path}' did not decode: {result}.");
		}

		width = decodedWidth;
		height = decodedHeight;
		return rgba;
	}

	public static byte[] EncodeRgbaFloat(ReadOnlySpan<float> rgba, int width, int height)
	{
		EnsurePixelCount(rgba.Length, width, height);

		ResultCode result = Exr.SaveEXRToMemory(rgba, width, height, ChannelsPerPixel, asFp16: false, out byte[] encoded);
		if (result != ResultCode.Success)
		{
			throw new InvalidDataException($"A {width}x{height} EXR did not encode: {result}.");
		}

		return encoded;
	}

	public static void EncodeRgbaFloat(string path, ReadOnlySpan<float> rgba, int width, int height)
	{
		EnsurePixelCount(rgba.Length, width, height);

		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

		ResultCode result = Exr.SaveEXR(rgba, width, height, ChannelsPerPixel, asFp16: false, path);
		if (result != ResultCode.Success)
		{
			throw new InvalidDataException($"A {width}x{height} EXR did not encode to '{path}': {result}.");
		}
	}

	private static void EnsurePixelCount(int actualFloats, int width, int height)
	{
		int expectedFloats = width * height * ChannelsPerPixel;
		if (actualFloats != expectedFloats)
		{
			throw new ArgumentException($"A {width}x{height} image needs {expectedFloats} floats but was handed {actualFloats}.");
		}
	}
}
