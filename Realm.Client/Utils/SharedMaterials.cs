using Godot;

namespace Realm.Client.Utils;

public static class SharedMaterials
{
    private const int PlayerColorGlowTextureSize = 128;

    private static System.Collections.Generic.Dictionary<Color, StandardMaterial3D> _sharedSelectionMaterials = new System.Collections.Generic.Dictionary<Color, StandardMaterial3D>();
    private static System.Collections.Generic.Dictionary<Color, StandardMaterial3D> _sharedPlayerColorGlowMaterials = new System.Collections.Generic.Dictionary<Color, StandardMaterial3D>();
    private static ImageTexture? _sharedPlayerColorGlowTexture;
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

    public static StandardMaterial3D GetOrCreatePlayerColorGlowMaterial(Color color)
    {
        if (!_sharedPlayerColorGlowMaterials.TryGetValue(color, out var material) || !GodotObject.IsInstanceValid(material))
        {
            material = new StandardMaterial3D
            {
                AlbedoColor = new Color(color.R, color.G, color.B, 0.38f),
                AlbedoTexture = GetOrCreatePlayerColorGlowTexture(),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                DisableReceiveShadows = true,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
            _sharedPlayerColorGlowMaterials[color] = material;
        }

        return material;
    }

    private static ImageTexture GetOrCreatePlayerColorGlowTexture()
    {
        if (_sharedPlayerColorGlowTexture != null && GodotObject.IsInstanceValid(_sharedPlayerColorGlowTexture))
            return _sharedPlayerColorGlowTexture;

        var image = Image.CreateEmpty(PlayerColorGlowTextureSize, PlayerColorGlowTextureSize, false, Image.Format.Rgba8);
        float center = (PlayerColorGlowTextureSize - 1) * 0.5f;

        for (int y = 0; y < PlayerColorGlowTextureSize; y++)
        {
            for (int x = 0; x < PlayerColorGlowTextureSize; x++)
            {
                float offsetX = (x - center) / center;
                float offsetY = (y - center) / center;
                float distance = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY);
                float falloff = Mathf.Clamp(1f - distance, 0f, 1f);
                image.SetPixel(x, y, new Color(1f, 1f, 1f, falloff * falloff));
            }
        }

        _sharedPlayerColorGlowTexture = ImageTexture.CreateFromImage(image);
        return _sharedPlayerColorGlowTexture;
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
        _sharedPlayerColorGlowMaterials.Clear();
        _sharedPlayerColorGlowTexture = null;
        _sharedHoverMaterial = null;
    }
}
