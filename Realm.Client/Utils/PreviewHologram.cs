using Godot;

namespace Realm.Client.Utils;

public static class PreviewHologram
{
	private static readonly Color DefaultUnitColor = new(0.15f, 0.65f, 1.0f);
	private static readonly Color DefaultPropColor = new(0.95f, 0.82f, 0.15f);

	public static Color UnitColor(bool isEnemy) => isEnemy ? new Color(1.0f, 0.3f, 0.15f) : DefaultUnitColor;

	public static Color PropColor() => DefaultPropColor;

	public static void Apply(Node root, Color color)
	{
		if (root == null || !GodotObject.IsInstanceValid(root)) return;

		if (root is MeshInstance3D meshInstance)
		{
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(color.R, color.G, color.B, 0.4f);
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.EmissionEnabled = true;
			material.Emission = new Color(color.R, color.G, color.B) * 0.5f;
			meshInstance.MaterialOverride = material;
		}

		foreach (var child in root.GetChildren())
		{
			Apply(child, color);
		}
	}
}
