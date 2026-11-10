using Godot;

namespace Realm.Client.UI;

[GlobalClass]
public partial class TacticalMap : Control
{
	private readonly Vector2[] _riverPoints = new Vector2[5];
	private readonly Vector2[] _diamondPoints = new Vector2[5];

	public override void _Draw()
	{
		Vector2 size = Size;

		Color riverColor = new Color(0.2f, 0.55f, 0.9f, 0.8f);
		Vector2[] riverPoints = _riverPoints;
		riverPoints[0] = new Vector2(0.2f * size.X, 0.45f * size.Y);
		riverPoints[1] = new Vector2(0.35f * size.X, 0.55f * size.Y);
		riverPoints[2] = new Vector2(0.5f * size.X, 0.65f * size.Y);
		riverPoints[3] = new Vector2(0.65f * size.X, 0.75f * size.Y);
		riverPoints[4] = new Vector2(0.85f * size.X, 0.82f * size.Y);
		for (int i = 0; i < riverPoints.Length - 1; i++)
		{
			DrawLine(riverPoints[i], riverPoints[i + 1], riverColor, 4f, true);
		}

		Vector2 playerPos = new Vector2(0.2f * size.X, 0.45f * size.Y);
		DrawCircle(playerPos, 10f, UIStyle.ColorGold);
		DrawCircle(playerPos, 7f, new Color(0.1f, 0.1f, 0.12f));
		DrawCircle(playerPos, 3f, UIStyle.ColorCyanGlow);

		Vector2 enemyPos = new Vector2(0.85f * size.X, 0.82f * size.Y);
		Vector2[] diamondPoints = _diamondPoints;
		diamondPoints[0] = new Vector2(enemyPos.X, enemyPos.Y - 10);
		diamondPoints[1] = new Vector2(enemyPos.X + 10, enemyPos.Y);
		diamondPoints[2] = new Vector2(enemyPos.X, enemyPos.Y + 10);
		diamondPoints[3] = new Vector2(enemyPos.X - 10, enemyPos.Y);
		diamondPoints[4] = new Vector2(enemyPos.X, enemyPos.Y - 10);
		DrawPolyline(diamondPoints, new Color(0.9f, 0.2f, 0.2f), 2.5f, true);
		DrawCircle(enemyPos, 3f, new Color(0.9f, 0.2f, 0.2f));

		DrawRect(new Rect2(Vector2.Zero, size), UIStyle.ColorBronze, false, 2.0f);
	}
}