using Godot;

namespace Realm.Client.Utils;

public static class SharedMaterials
{
    private static System.Collections.Generic.Dictionary<Color, StandardMaterial3D> _sharedSelectionMaterials = new System.Collections.Generic.Dictionary<Color, StandardMaterial3D>();
    private static System.Collections.Generic.Dictionary<Color, StandardMaterial3D> _sharedPlayerColorRingMaterials = new System.Collections.Generic.Dictionary<Color, StandardMaterial3D>();
    private static StandardMaterial3D? _sharedHoverMaterial;

    public static StandardMaterial3D GetOrCreateSelectionMaterial(Color color)
    {
        if (!_sharedSelectionMaterials.TryGetValue(color, out var material) || !GodotObject.IsInstanceValid(material))
        {
            material = new StandardMaterial3D
            {
                AlbedoColor = color,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                DisableReceiveShadows = true,
                EmissionEnabled = false,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            _sharedSelectionMaterials[color] = material;
        }

        return material;
    }

    public static StandardMaterial3D GetOrCreatePlayerColorRingMaterial(Color color)
    {
        if (!_sharedPlayerColorRingMaterials.TryGetValue(color, out var material) || !GodotObject.IsInstanceValid(material))
        {
            material = new StandardMaterial3D
            {
                AlbedoColor = new Color(color.R, color.G, color.B, 0.55f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                DisableReceiveShadows = true,
                EmissionEnabled = true,
                Emission = color,
                EmissionEnergyMultiplier = 0.5f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            _sharedPlayerColorRingMaterials[color] = material;
        }

        return material;
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

    public static void ClearCache()
    {
        _sharedSelectionMaterials.Clear();
        _sharedPlayerColorRingMaterials.Clear();
        _sharedHoverMaterial = null;
    }
}
