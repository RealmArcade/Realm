using Godot;

namespace Realm.Client.Utils;

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

	public static void ApplySlopeAlignment(Godot.Node3D mainNode, Godot.Node3D? visualNode, bool isSlopeAligned)
	{
		if (isSlopeAligned && Realm.Client.Core.GameHost.Instance?.GroundTerrain != null && visualNode != null && Godot.GodotObject.IsInstanceValid(visualNode))
		{
			Realm.Client.Core.GameHost.Instance.GroundTerrain.GetHeightAndNormal(mainNode.GlobalPosition.X, mainNode.GlobalPosition.Z, out _, out Godot.Vector3 normal);
			if (normal.LengthSquared() > 0.01f)
			{
				normal = normal.Normalized();
				Godot.Vector3 up = normal;
				Godot.Vector3 forward = System.MathF.Abs(up.Y) < 0.99f ? Godot.Vector3.Up.Cross(up).Cross(up).Normalized() : -Godot.Vector3.Forward;
				Godot.Vector3 right = up.Cross(forward).Normalized();
				visualNode.Basis = new Godot.Basis(right, up, -forward);
			}
		}
	}
}