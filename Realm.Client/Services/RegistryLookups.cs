using Godot;
using Realm.Ecs.Common;

namespace Realm.Client.Services;

/// <summary>
///     Reading a definition's presentation settings out of the registries: collision radius, brightness,
///     colour tint and death shader, and the luminance flag the editor toggles across all four registry
///     kinds for one asset key.
/// </summary>
public static class RegistryLookups
{
	public static float GetRegistryCollisionCircleRatio(RegistryService registries, string primaryKey)
	{
		if (string.IsNullOrEmpty(primaryKey)) return 1.0f;

		if (registries.UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.CollisionCircle > 0f) return meta.CollisionCircle;
		if (registries.BuildingRegistry.TryGetValue(primaryKey, out var buildingMeta) && buildingMeta.CollisionCircle > 0f) return buildingMeta.CollisionCircle;
		if (registries.ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.CollisionCircle > 0f) return resMeta.CollisionCircle;
		if (registries.PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.CollisionCircle > 0f) return propMeta.CollisionCircle;

		return 1.0f;
	}

	public static bool TryGetRegistryBrightness(RegistryService registries, string key, out float brightness)
	{
		brightness = 0f;
		if (registries.UnitRegistry.TryGetValue(key, out var meta) && meta.Brightness > 0f)
		{
			brightness = meta.Brightness;
			return true;
		}

		if (registries.BuildingRegistry.TryGetValue(key, out var buildingMeta) && buildingMeta.Brightness > 0f)
		{
			brightness = buildingMeta.Brightness;
			return true;
		}

		if (registries.ResourceRegistry.TryGetValue(key, out var resMeta) && resMeta.Brightness > 0f)
		{
			brightness = resMeta.Brightness;
			return true;
		}

		if (registries.PropRegistry.TryGetValue(key, out var propMeta) && propMeta.Brightness > 0f)
		{
			brightness = propMeta.Brightness;
			return true;
		}

		return false;
	}

	public static bool TryGetRegistryColorTint(RegistryService registries, string key, out Color color)
	{
		color = default;
		if (registries.UnitRegistry.TryGetValue(key, out var meta) && !string.IsNullOrEmpty(meta.Tint) && Color.HtmlIsValid(meta.Tint))
		{
			color = Color.FromHtml(meta.Tint);
			return true;
		}

		if (registries.BuildingRegistry.TryGetValue(key, out var buildingMeta) && !string.IsNullOrEmpty(buildingMeta.Tint) && Color.HtmlIsValid(buildingMeta.Tint))
		{
			color = Color.FromHtml(buildingMeta.Tint);
			return true;
		}

		if (registries.ResourceRegistry.TryGetValue(key, out var resMeta) && !string.IsNullOrEmpty(resMeta.Tint) && Color.HtmlIsValid(resMeta.Tint))
		{
			color = Color.FromHtml(resMeta.Tint);
			return true;
		}

		if (registries.PropRegistry.TryGetValue(key, out var propMeta) && !string.IsNullOrEmpty(propMeta.Tint) && Color.HtmlIsValid(propMeta.Tint))
		{
			color = Color.FromHtml(propMeta.Tint);
			return true;
		}

		return false;
	}

	public static void UpdateLuminanceInRegistries(RegistryService registries, string assetKey, bool normalizeLuminance)
	{
		if (registries.UnitRegistry.TryGetValue(assetKey, out var unitMeta))
		{
			unitMeta.NormalizeLuminance = normalizeLuminance;
			registries.UnitRegistry[assetKey] = unitMeta;
		}

		if (registries.BuildingRegistry.TryGetValue(assetKey, out var bldMeta))
		{
			bldMeta.NormalizeLuminance = normalizeLuminance;
			registries.BuildingRegistry[assetKey] = bldMeta;
		}

		if (registries.ResourceRegistry.TryGetValue(assetKey, out var resMeta))
		{
			resMeta.NormalizeLuminance = normalizeLuminance;
			registries.ResourceRegistry[assetKey] = resMeta;
		}

		if (registries.PropRegistry.TryGetValue(assetKey, out var propMeta))
		{
			propMeta.NormalizeLuminance = normalizeLuminance;
			registries.PropRegistry[assetKey] = propMeta;
		}
	}

	public static bool TryGetRegistryDeathShader(RegistryService registries, string key, out string shader)
	{
		shader = "";
		if (registries.UnitRegistry.TryGetValue(key, out var meta) && !string.IsNullOrWhiteSpace(meta.DeathShader))
		{
			shader = meta.DeathShader;
			return true;
		}

		if (registries.BuildingRegistry.TryGetValue(key, out var bldMeta) && !string.IsNullOrWhiteSpace(bldMeta.DeathShader))
		{
			shader = bldMeta.DeathShader;
			return true;
		}

		if (registries.ResourceRegistry.TryGetValue(key, out var resMeta) && !string.IsNullOrWhiteSpace(resMeta.DeathShader))
		{
			shader = resMeta.DeathShader;
			return true;
		}

		if (registries.PropRegistry.TryGetValue(key, out var propMeta) && !string.IsNullOrWhiteSpace(propMeta.DeathShader))
		{
			shader = propMeta.DeathShader;
			return true;
		}

		return false;
	}
}
