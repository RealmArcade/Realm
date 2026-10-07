using Godot;
using Realm.Shared.Metadata;
using Realm.Shared.Terrain;

namespace Realm.Godot.Utils;

public static class ModelExtensions
{
	public static Vector3 ToGodotVector3(this Vector3Data v) => new(v.X, v.Y, v.Z);
	public static Vector3 ToGodotVector3(this Vector3Data? v, Vector3 defaultValue = default) => v.HasValue ? new(v.Value.X, v.Value.Y, v.Value.Z) : defaultValue;
	public static Vector3Data ToVector3Data(this Vector3 v) => new(v.X, v.Y, v.Z);

	public static Vector2 ToGodotVector2(this Vector2Data v) => new(v.X, v.Y);
	public static Vector2 ToGodotVector2(this Vector2Data? v, Vector2 defaultValue = default) => v.HasValue ? new(v.Value.X, v.Value.Y) : defaultValue;
	public static Vector2Data ToVector2Data(this Vector2 v) => new(v.X, v.Y);

	public static Color GetSunColor(this EnvironmentPresetConfig config)
	{
		return Color.HtmlIsValid(config.SunColorHex) ? Color.FromHtml(config.SunColorHex) : new Color(1.000f, 0.980f, 0.940f);
	}

	public static Color GetAmbientColor(this EnvironmentPresetConfig config)
	{
		return Color.HtmlIsValid(config.AmbientColorHex) ? Color.FromHtml(config.AmbientColorHex) : new Color(0.480f, 0.580f, 0.740f);
	}

	public static Color GetFogColor(this EnvironmentPresetConfig config)
	{
		return Color.HtmlIsValid(config.FogColorHex) ? Color.FromHtml(config.FogColorHex) : new Color(0.550f, 0.650f, 0.750f);
	}
}
