using Godot;

namespace Realm.Client.Utils;

public static class SharedMaterials
{
	private static StandardMaterial3D _sharedSelectionMaterial;
	private static StandardMaterial3D _sharedHoverMaterial;

	public static StandardMaterial3D GetOrCreateSelectionMaterial(Color? color = null)
	{
		var finalColor = color ?? new Color(0.22f, 0.54f, 0.26f);
		if (_sharedSelectionMaterial == null || !GodotObject.IsInstanceValid(_sharedSelectionMaterial) || _sharedSelectionMaterial.AlbedoColor != finalColor)
		{
			_sharedSelectionMaterial = new StandardMaterial3D
			{
				AlbedoColor = finalColor,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				DisableReceiveShadows = true,
				EmissionEnabled = false,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
		}

		return _sharedSelectionMaterial;
	}

	public static StandardMaterial3D GetOrCreateHoverMaterial()
	{
		if (_sharedHoverMaterial == null || !GodotObject.IsInstanceValid(_sharedHoverMaterial))
		{
			_sharedHoverMaterial = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.88f, 0.88f, 0.88f, 0.22f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				DisableReceiveShadows = true,
				EmissionEnabled = false,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
		}

		return _sharedHoverMaterial;
	}
}
