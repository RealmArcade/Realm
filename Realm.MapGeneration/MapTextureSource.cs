using System;
using System.Globalization;

namespace Realm.MapGeneration;

public readonly record struct MapTextureSource(
	int SwatchIndex,
	string Name,
	string FullPath,
	float ScaleFactor,
	float Brightness,
	float TintRed,
	float TintGreen,
	float TintBlue,
	int DefaultPathingCode)
{
	public TerrainSurfaceKind Kind => TerrainSurfaceClassifier.Classify(Name);

	public static MapTextureSource Empty(int swatchIndex)
	{
		return new MapTextureSource(swatchIndex, string.Empty, string.Empty, 4f, 1f, 1f, 1f, 1f, 44);
	}

	public static (float Red, float Green, float Blue) ParseTint(string? tint)
	{
		if (string.IsNullOrWhiteSpace(tint) || tint.Length < 7 || tint[0] != '#')
		{
			return (1f, 1f, 1f);
		}

		bool parsedRed = byte.TryParse(tint.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte redComponent);
		bool parsedGreen = byte.TryParse(tint.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte greenComponent);
		bool parsedBlue = byte.TryParse(tint.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blueComponent);
		if (!parsedRed || !parsedGreen || !parsedBlue)
		{
			return (1f, 1f, 1f);
		}

		return (redComponent / 255f, greenComponent / 255f, blueComponent / 255f);
	}
}
