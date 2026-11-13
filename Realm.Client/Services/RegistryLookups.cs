using Godot;
using Realm.Shared.Metadata;

namespace Realm.Client.Services;

/// <summary>
///     Reading a definition's presentation settings out of the one placeable store: collision radius, brightness,
///     colour tint and death shader, and the luminance flag the editor toggles across every placeable kind at once.
/// </summary>
public static class RegistryLookups
{
	public static float GetRegistryCollisionCircleRatio(RegistryService registries, string primaryKey)
	{
		if (string.IsNullOrEmpty(primaryKey)) return 1.0f;
		if (registries.Placeables.Find(primaryKey) is not { } metadata) return 1.0f;

		return metadata.CollisionCircle > 0f ? metadata.CollisionCircle : 1.0f;
	}

	public static bool TryGetRegistryBrightness(RegistryService registries, string key, out float brightness)
	{
		brightness = 0f;
		if (registries.Placeables.Find(key) is not { } metadata || metadata.Brightness <= 0f) return false;

		brightness = metadata.Brightness;
		return true;
	}

	public static bool TryGetRegistryColorTint(RegistryService registries, string key, out Color color)
	{
		color = default;
		if (registries.Placeables.Find(key) is not { } metadata) return false;
		if (string.IsNullOrEmpty(metadata.Tint) || !Color.HtmlIsValid(metadata.Tint)) return false;

		color = Color.FromHtml(metadata.Tint);
		return true;
	}

	public static void UpdateLuminanceInRegistries(RegistryService registries, string assetKey, bool normalizeLuminance)
	{
		if (registries.Placeables.Find(assetKey) is not { } metadata) return;

		metadata.NormalizeLuminance = normalizeLuminance;
	}

	public static bool TryGetRegistryDeathShader(RegistryService registries, string key, out string shader)
	{
		shader = "";
		if (registries.Placeables.Find(key) is not { } metadata) return false;
		if (string.IsNullOrWhiteSpace(metadata.DeathShader)) return false;

		shader = metadata.DeathShader;
		return true;
	}
}
