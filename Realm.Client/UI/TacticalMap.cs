using Godot;

namespace Realm.Client.UI;

public partial class TacticalMap : Control
{
	public override void _Draw()
	{
		Vector2 size = Size;
		

		Color riverColor = new Color(0.2f, 0.55f, 0.9f, 0.8f);
		Vector2[] riverPoints = new[]
		{
			new Vector2(0.2f * size.X, 0.25f * size.Y),
			new Vector2(0.35f * size.X, 0.35f * size.Y),
			new Vector2(0.5f * size.X, 0.55f * size.Y),
			new Vector2(0.65f * size.X, 0.65f * size.Y),
			new Vector2(0.85f * size.X, 0.7f * size.Y)
		};
		for (int i = 0; i < riverPoints.Length - 1; i++)
		{
			DrawLine(riverPoints[i], riverPoints[i+1], riverColor, 4f, true);
		}


		Vector2 playerPos = new Vector2(0.2f * size.X, 0.25f * size.Y);
		DrawCircle(playerPos, 10f, UIStyle.ColorGold);
		DrawCircle(playerPos, 7f, new Color(0.1f, 0.1f, 0.12f));
		DrawCircle(playerPos, 3f, UIStyle.ColorCyanGlow);


		Vector2 enemyPos = new Vector2(0.85f * size.X, 0.7f * size.Y);
		Vector2[] diamondPoints = new[]
		{
			new Vector2(enemyPos.X, enemyPos.Y - 10),
			new Vector2(enemyPos.X + 10, enemyPos.Y),
			new Vector2(enemyPos.X, enemyPos.Y + 10),
			new Vector2(enemyPos.X - 10, enemyPos.Y),
			new Vector2(enemyPos.X, enemyPos.Y - 10)
		};
		DrawPolyline(diamondPoints, new Color(0.9f, 0.2f, 0.2f), 2.5f, true);
		DrawCircle(enemyPos, 3f, new Color(0.9f, 0.2f, 0.2f));


		DrawRect(new Rect2(Vector2.Zero, size), UIStyle.ColorBronze, false, 2.0f);
	}
}