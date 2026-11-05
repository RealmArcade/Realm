using System;
using Godot;

namespace Realm.Client.Utils;

/// <summary>
///     Stand-in artwork for things that have no real icon yet. Each entry is generated once at runtime and cached, and
///     is addressed by a <c>placeholder://</c> path so tooltip markup can reference it exactly like a file icon.
///     Replace an entry with the real asset path once the art exists.
/// </summary>
public static class PlaceholderIcons
{
	public const string ManaPath = "placeholder://mana";

	private const int CircleSize = 32;

	private static readonly Color ManaFill = new(0.29f, 0.55f, 1f);
	private static readonly Color ManaRim = new(0.10f, 0.24f, 0.66f);

	private static Texture2D? _mana;

	public static Texture2D Mana => _mana ??= CreateCircle(ManaFill, ManaRim);

	public static bool TryResolve(string? path, out Texture2D? texture)
	{
		texture = null;
		if (string.IsNullOrWhiteSpace(path)) return false;
		if (!string.Equals(path.Trim(), ManaPath, StringComparison.OrdinalIgnoreCase)) return false;

		texture = Mana;
		return true;
	}

	private static Texture2D CreateCircle(Color fill, Color rim)
	{
		var image = Image.CreateEmpty(CircleSize, CircleSize, false, Image.Format.Rgba8);
		image.Fill(new Color(0f, 0f, 0f, 0f));

		float center = (CircleSize - 1) * 0.5f;
		float radius = (CircleSize * 0.5f) - 0.5f;
		float rimWidth = MathF.Max(2f, CircleSize * 0.09f);

		for (int y = 0; y < CircleSize; y++)
		{
			for (int x = 0; x < CircleSize; x++)
			{
				float dx = x - center;
				float dy = y - center;
				float distance = MathF.Sqrt((dx * dx) + (dy * dy));

				// One pixel of coverage at the edge keeps the circle from looking stepped at 16 px.
				float coverage = Math.Clamp(radius - distance + 0.5f, 0f, 1f);
				if (coverage <= 0f) continue;

				Color color = distance >= radius - rimWidth ? rim : fill;
				color.A *= coverage;
				image.SetPixel(x, y, color);
			}
		}

		return ImageTexture.CreateFromImage(image);
	}
}
