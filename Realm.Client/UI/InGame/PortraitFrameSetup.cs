using Godot;
using Realm.Client.Utils;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The portrait frame before anything is selected: the map's flag behind the mask shader, and the name line saying
///     there is no selection. Both are fixed scene furniture, so they are painted once at startup and never again.
/// </summary>
public static class PortraitFrameSetup
{
	private const string PortraitTexturePath = "BottomConsole/HBox/PortraitFrame/VBox/PortraitTexture";
	private const string UnitNamePath = "BottomConsole/HBox/PortraitFrame/VBox/UnitName";
	private const string FlagAsset = "res://Assets/UI/alliance_flag.webp";
	private const string MaskShaderAsset = "res://Assets/shaders/hud_portrait_mask.gdshader";

	private static Shader? _maskShader;

	public static void Prepare(Node root)
	{
		if (root.GetNodeOrNull<TextureRect>(PortraitTexturePath) is { } portrait)
		{
			portrait.Texture = TrimmedTextureLoader.LoadTrimmedTexture(FlagAsset);
			_maskShader ??= GD.Load<Shader>(MaskShaderAsset);
			portrait.Material = new ShaderMaterial { Shader = _maskShader };
		}

		root.GetNode<Label>(UnitNamePath).Text = TranslationServer.Translate("No Selection");
	}
}
