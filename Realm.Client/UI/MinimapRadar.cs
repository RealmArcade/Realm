using Godot;

namespace Realm.Client.UI;

public partial class MinimapRadar : Control
{
	public override void _Draw()
	{
		Vector2 size = Size;
		var radarTex = MinimapOverlay.GetRadarTexture();
		DrawTextureRect(radarTex, new Rect2(Vector2.Zero, size), false);
	}
}