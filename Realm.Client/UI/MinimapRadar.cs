using Godot;

namespace Realm.Client.UI;

public partial class MinimapRadar : Control
{
	public override void _Draw()
	{
		Vector2 size = Size;
		DrawRect(new Rect2(Vector2.Zero, size), new Color(0.05f, 0.07f, 0.05f), true); // Deep green backdrop
		Color radarColor = new Color(0.1f, 0.5f, 0.15f, 0.35f);
		
		DrawLine(new Vector2(size.X / 2f, 0), new Vector2(size.X / 2f, size.Y), radarColor, 1.0f);
		DrawLine(new Vector2(0, size.Y / 2f), new Vector2(size.X, size.Y / 2f), radarColor, 1.0f);

		DrawCircle(size / 2f, size.X * 0.45f, radarColor, false, 1.5f);
		DrawCircle(size / 2f, size.X * 0.3f, radarColor, false, 1.0f);
		DrawCircle(size / 2f, size.X * 0.15f, radarColor, false, 1.0f);
		
		DrawRect(new Rect2(Vector2.Zero, size), UIStyle.ColorBronze, false, 1.5f);
	}
}