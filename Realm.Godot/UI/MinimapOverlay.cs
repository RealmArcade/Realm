using Godot;

public partial class MinimapOverlay : Control
{
	public override void _Draw()
	{
		if (GameHost.Instance == null) return;

		var size = Size;

		if (InGameHUD.Instance != null && !InGameHUD.Instance.ShowMinimapTerrain)
		{
			DrawRect(new Rect2(Vector2.Zero, size), new Color(0.04f, 0.08f, 0.04f), true);
			Color radarGridColor = new Color(0.1f, 0.4f, 0.15f, 0.3f);

			DrawLine(new Vector2(size.X / 2f, 0), new Vector2(size.X / 2f, size.Y), radarGridColor, 1.0f);
			DrawLine(new Vector2(0, size.Y / 2f), new Vector2(size.X, size.Y / 2f), radarGridColor, 1.0f);

			DrawCircle(size / 2f, size.X * 0.45f, radarGridColor, false, 1.5f);
			DrawCircle(size / 2f, size.X * 0.3f, radarGridColor, false, 1.0f);
			DrawCircle(size / 2f, size.X * 0.15f, radarGridColor, false, 1.0f);

			DrawRect(new Rect2(Vector2.Zero, size), UIStyle.ColorBronze, false, 1.5f);
		}

		if (InGameHUD.Instance != null && !string.Equals(InGameHUD.Instance.ShroudType, "visible", System.StringComparison.OrdinalIgnoreCase))
		{
			float cellWidth = size.X / 32f;
			float cellHeight = size.Y / 32f;
			var grid = InGameHUD.Instance.ShroudGrid;
			for (int x = 0; x < 32; x++)
			{
				for (int z = 0; z < 32; z++)
				{
					byte val = grid[x, z];
					if (val == Realm.Ecs.Components.Terrain.ShroudState.ExplorationShroud)
					{
						var rect = new Rect2(new Vector2(x * cellWidth, z * cellHeight), new Vector2(cellWidth, cellHeight));
						DrawRect(rect, new Color(0f, 0f, 0f, 1.0f), true);
					}
					else if (val == Realm.Ecs.Components.Terrain.ShroudState.VisionShroud)
					{
						var rect = new Rect2(new Vector2(x * cellWidth, z * cellHeight), new Vector2(cellWidth, cellHeight));
						DrawRect(rect, new Color(0f, 0f, 0f, 0.33f), true);
					}
				}
			}
		}

		var (physicalWidth, physicalDepth, _) = MinimapHelper.GetTerrainDimensions();

		foreach (var unit in GameHost.Instance.AllUnits)
		{
			if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;

			if (unit.IsEnemy && InGameHUD.Instance != null && !string.Equals(InGameHUD.Instance.ShroudType, "visible", System.StringComparison.OrdinalIgnoreCase))
			{
				int gx = (int)Mathf.Clamp((unit.GlobalPosition.X / physicalWidth + 0.5f) * 32, 0, 31);
				int gz = (int)Mathf.Clamp((unit.GlobalPosition.Z / physicalDepth + 0.5f) * 32, 0, 31);
				if (InGameHUD.Instance.ShroudGrid[gx, gz] != Realm.Ecs.Components.Terrain.ShroudState.Visible)
				{
					continue;
				}
			}

			Vector2 drawPos = MinimapHelper.WorldToMinimap(unit.GlobalPosition, size);

			Color color = unit.IsEnemy ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.2f, 0.6f, 1.0f);
			float iconSize = 5.0f;

			if (unit.IsBuilding)
			{
				iconSize = 8.0f;
				var rect = new Rect2(drawPos - new Vector2(iconSize / 2f, iconSize / 2f), new Vector2(iconSize, iconSize));
				DrawRect(rect, color, true);
				DrawRect(rect, new Color(0f, 0f, 0f, 0.6f), false, 1.0f);
			}
			else
			{
				color = new Color(0.9f, 0.3f, 0.1f);
				DrawCircle(drawPos, iconSize, color);
				DrawCircle(drawPos, iconSize, new Color(0f, 0f, 0f, 0.6f), false, 1.0f);
			}

			if (unit.IsSelected)
			{
				Color selColor = unit.IsEnemy ? new Color(0.9f, 0.1f, 0.2f) : new Color(0.22f, 0.54f, 0.26f);
				DrawCircle(drawPos, iconSize + 2.5f, selColor, false, 1.2f);
			}
		}

		foreach (var ping in GameHost.Instance.ActivePings)
		{
			Vector2 drawPos = MinimapHelper.WorldToMinimap(ping.WorldPos, size);

			float pulse = Mathf.Sin(ping.LifeTime * 15f) * 0.5f + 1.0f;
			float radius = 12f * pulse;

			DrawCircle(drawPos, radius, new Color(1f, 0.1f, 0.1f, 0.5f), false, 2.0f);
			DrawCircle(drawPos, radius - 4f, new Color(1f, 0.1f, 0.1f, 0.2f), true);
		}
	}
}