using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.UI;

public partial class MinimapOverlay : Control
{
	private static Texture2D? _unitCircleTexture;
	private const float UnitCircleTextureScale = 32f / 30f;

	private static readonly Color EnemyColor = new Color(0.9f, 0.1f, 0.1f);
	private static readonly Color AllyColor = new Color(0.2f, 0.6f, 1.0f);
	private static readonly Color BuildingOutlineColor = new Color(0f, 0f, 0f, 0.6f);
	private static readonly Color SelectedSpectatorColor = new Color(1.0f, 1.0f, 1.0f);
	private static readonly Color SelectedEnemyColor = new Color(0.9f, 0.1f, 0.2f);
	private static readonly Color SelectedAllyColor = new Color(0.22f, 0.54f, 0.26f);
	private static readonly Color ExplorationShroudColor = new Color(0f, 0f, 0f, 1.0f);
	private static readonly Color VisionShroudColor = new Color(0f, 0f, 0f, 0.33f);

	private static readonly Color PlayerOneColor = new Color(0.2f, 0.6f, 1.0f);
	private static readonly Color PlayerTwoColor = new Color(0.9f, 0.2f, 0.2f);
	private static readonly Color PlayerThreeColor = new Color(0.2f, 0.8f, 0.3f);
	private static readonly Color PlayerFourColor = new Color(0.9f, 0.8f, 0.2f);
	private static readonly Color DefaultPlayerColor = new Color(0.7f, 0.7f, 0.7f);

	public override void _Draw()
	{
		if (GameHost.Instance == null) return;

		var size = Size;
		var (physicalWidth, physicalDepth, _) = MinimapHelper.GetTerrainDimensions();

		DrawRadarBackground(size);
		DrawShroudGrid(size);
		DrawAllUnits(size, physicalWidth, physicalDepth);
		DrawActivePings(size);
	}

	private void DrawRadarBackground(Vector2 size)
	{
		if (InGameHUD.Instance == null || InGameHUD.Instance.ShowMinimapTerrain) return;

		DrawRect(new Rect2(Vector2.Zero, size), new Color(0.04f, 0.08f, 0.04f), true);
		Color radarGridColor = new Color(0.1f, 0.4f, 0.15f, 0.3f);

		DrawLine(new Vector2(size.X / 2f, 0), new Vector2(size.X / 2f, size.Y), radarGridColor, 1.0f);
		DrawLine(new Vector2(0, size.Y / 2f), new Vector2(size.X, size.Y / 2f), radarGridColor, 1.0f);

		DrawCircle(size / 2f, size.X * 0.45f, radarGridColor, false, 1.5f);
		DrawCircle(size / 2f, size.X * 0.3f, radarGridColor, false, 1.0f);
		DrawCircle(size / 2f, size.X * 0.15f, radarGridColor, false, 1.0f);

		DrawRect(new Rect2(Vector2.Zero, size), UIStyle.ColorBronze, false, 1.2f);
	}

	private void DrawShroudGrid(Vector2 size)
	{
		if (InGameHUD.Instance == null || string.Equals(InGameHUD.Instance.ShroudType, "visible", StringComparison.OrdinalIgnoreCase)) return;

		float cellWidth = size.X / 32f;
		float cellHeight = size.Y / 32f;
		var grid = InGameHUD.Instance.ShroudGrid;
		for (int x = 0; x < 32; x++)
		{
			for (int z = 0; z < 32; z++)
			{
				byte val = grid[x, z];
				if (val == ShroudState.ExplorationShroud)
				{
					var rect = new Rect2(x * cellWidth, z * cellHeight, cellWidth, cellHeight);
					DrawRect(rect, ExplorationShroudColor, true);
				}
				else if (val == ShroudState.VisionShroud)
				{
					var rect = new Rect2(x * cellWidth, z * cellHeight, cellWidth, cellHeight);
					DrawRect(rect, VisionShroudColor, true);
				}
			}
		}
	}

	private void DrawAllUnits(Vector2 size, float physicalWidth, float physicalDepth)
	{
		var allUnits = GameHost.Instance?.AllUnits;
		if (allUnits == null || allUnits.Count == 0) return;

		var circleTexture = GetUnitCircleTexture();
		bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
		var hud = InGameHUD.Instance;
		bool isAlwaysVisible = hud == null || string.Equals(hud.ShroudType, "visible", StringComparison.OrdinalIgnoreCase);
		var shroudGrid = isAlwaysVisible ? null : hud?.ShroudGrid;
		float invPhysicalWidth = physicalWidth > 0f ? 1f / physicalWidth : 0f;
		float invPhysicalDepth = physicalDepth > 0f ? 1f / physicalDepth : 0f;

		int count = allUnits.Count;
		for (int i = 0; i < count; i++)
		{
			var unit = allUnits[i];
			DrawSingleUnit(unit, size, invPhysicalWidth, invPhysicalDepth, isSpectator, shroudGrid, circleTexture);
		}
	}

	private void DrawSingleUnit(Unit3D unit, Vector2 size, float invPhysicalWidth, float invPhysicalDepth, bool isSpectator, byte[,]? shroudGrid, Texture2D circleTexture)
	{
		if (unit == null || !GodotObject.IsInstanceValid(unit)) return;

		var pos = unit.GlobalPosition;
		float normX = pos.X * invPhysicalWidth + 0.5f;
		float normZ = pos.Z * invPhysicalDepth + 0.5f;

		if (!isSpectator && shroudGrid != null)
		{
			bool isEnemy = GameContext.PerspectiveFilter.IsHostileToObserved(unit.Player);
			if (isEnemy)
			{
				int gx = (int)Mathf.Clamp(normX * 32f, 0f, 31f);
				int gz = (int)Mathf.Clamp(normZ * 32f, 0f, 31f);
				if (shroudGrid[gx, gz] != ShroudState.Visible)
				{
					return;
				}
			}
		}

		float drawX = Mathf.Clamp(normX, 0f, 1f) * size.X;
		float drawY = Mathf.Clamp(normZ, 0f, 1f) * size.Y;
		var drawPos = new Vector2(drawX, drawY);
		float iconSize = unit.IsBuilding ? 8.0f : 5.0f;

		DrawUnitIcon(unit, drawPos, iconSize, circleTexture, isSpectator);
		DrawSelectionRing(unit, drawPos, iconSize, isSpectator);
	}

	private void DrawSingleUnit(Unit3D unit, Vector2 size, float physicalWidth, float physicalDepth)
	{
		if (unit == null || !GodotObject.IsInstanceValid(unit)) return;
		if (!IsUnitVisibleOnMinimap(unit, physicalWidth, physicalDepth)) return;

		Vector2 drawPos = MinimapHelper.WorldToMinimap(unit.GlobalPosition, size);
		float iconSize = unit.IsBuilding ? 8.0f : 5.0f;

		DrawUnitIcon(unit, drawPos, iconSize);
		DrawSelectionRing(unit, drawPos, iconSize);
	}

	private bool IsUnitVisibleOnMinimap(Unit3D unit, float physicalWidth, float physicalDepth)
	{
		bool isSpectator = GameContext.PerspectiveFilter.IsSpectating;
		if (isSpectator) return true;

		bool isEnemy = GameContext.PerspectiveFilter.IsHostileToObserved(unit.Player);
		if (!isEnemy) return true;
		if (InGameHUD.Instance == null) return true;
		if (string.Equals(InGameHUD.Instance.ShroudType, "visible", StringComparison.OrdinalIgnoreCase)) return true;

		int gx = (int)Mathf.Clamp((unit.GlobalPosition.X / physicalWidth + 0.5f) * 32, 0, 31);
		int gz = (int)Mathf.Clamp((unit.GlobalPosition.Z / physicalDepth + 0.5f) * 32, 0, 31);
		return InGameHUD.Instance.ShroudGrid[gx, gz] == ShroudState.Visible;
	}

	private void DrawUnitIcon(Unit3D unit, Vector2 drawPos, float iconSize, Texture2D circleTexture, bool isSpectator)
	{
		Color color;
		if (isSpectator)
		{
			color = GetPlayerMinimapColor(unit.Player);
		}
		else
		{
			bool isEnemy = GameContext.PerspectiveFilter.IsHostileToObserved(unit.Player);
			color = isEnemy ? EnemyColor : AllyColor;
		}

		if (unit.IsBuilding)
		{
			var rect = new Rect2(drawPos.X - iconSize * 0.5f, drawPos.Y - iconSize * 0.5f, iconSize, iconSize);
			DrawRect(rect, color, true);
			DrawRect(rect, BuildingOutlineColor, false, 1.0f);
			return;
		}

		float halfExtent = iconSize * UnitCircleTextureScale;
		var circleRect = new Rect2(drawPos.X - halfExtent, drawPos.Y - halfExtent, halfExtent * 2f, halfExtent * 2f);
		DrawTextureRect(circleTexture, circleRect, false, color);
	}

	private void DrawUnitIcon(Unit3D unit, Vector2 drawPos, float iconSize)
	{
		DrawUnitIcon(unit, drawPos, iconSize, GetUnitCircleTexture(), GameContext.PerspectiveFilter.IsSpectating);
	}

	private static Texture2D GetUnitCircleTexture()
	{
		if (_unitCircleTexture != null && GodotObject.IsInstanceValid(_unitCircleTexture))
		{
			return _unitCircleTexture;
		}

		const int textureSize = 64;
		const float center = textureSize * 0.5f;
		const float outerRadius = 30f;
		const float strokeWidth = 6f;
		const float innerRadius = outerRadius - strokeWidth;
		const float antialiasMargin = 0.75f;

		var image = Image.CreateEmpty(textureSize, textureSize, false, Image.Format.Rgba8);
		for (int y = 0; y < textureSize; y++)
		{
			for (int x = 0; x < textureSize; x++)
			{
				float deltaX = x + 0.5f - center;
				float deltaY = y + 0.5f - center;
				float dist = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);

				if (dist <= innerRadius - antialiasMargin)
				{
					image.SetPixel(x, y, new Color(1f, 1f, 1f, 1f));
				}
				else if (dist < innerRadius + antialiasMargin)
				{
					float factor = (dist - (innerRadius - antialiasMargin)) / (antialiasMargin * 2f);
					float brightness = 1f - factor;
					float alpha = 1f - 0.4f * factor;
					image.SetPixel(x, y, new Color(brightness, brightness, brightness, alpha));
				}
				else if (dist <= outerRadius - antialiasMargin)
				{
					image.SetPixel(x, y, new Color(0f, 0f, 0f, 0.6f));
				}
				else if (dist < outerRadius + antialiasMargin)
				{
					float factor = (dist - (outerRadius - antialiasMargin)) / (antialiasMargin * 2f);
					float alpha = 0.6f * (1f - factor);
					image.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
				}
				else
				{
					image.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
				}
			}
		}

		image.GenerateMipmaps();
		_unitCircleTexture = ImageTexture.CreateFromImage(image);
		return _unitCircleTexture;
	}

	private Color GetPlayerMinimapColor(int playerIndex)
	{
		var playerList = LobbyManager.Instance?.PlayerList;
		if (playerList != null)
		{
			for (int i = 0; i < playerList.Count; i++)
			{
				var p = playerList[i];
				if (p != null && p.Slot == playerIndex)
				{
					return p.Color;
				}
			}
		}

		return playerIndex switch
		{
			1 => PlayerOneColor,
			2 => PlayerTwoColor,
			3 => PlayerThreeColor,
			4 => PlayerFourColor,
			_ => DefaultPlayerColor
		};
	}

	private void DrawSelectionRing(Unit3D unit, Vector2 drawPos, float iconSize, bool isSpectator)
	{
		if (!unit.IsSelected) return;

		Color selColor;
		if (isSpectator)
		{
			selColor = SelectedSpectatorColor;
		}
		else
		{
			bool isEnemy = GameContext.PerspectiveFilter.IsHostileToObserved(unit.Player);
			selColor = isEnemy ? SelectedEnemyColor : SelectedAllyColor;
		}
		DrawCircle(drawPos, iconSize + 2.5f, selColor, false, 1.2f);
	}

	private void DrawSelectionRing(Unit3D unit, Vector2 drawPos, float iconSize)
	{
		DrawSelectionRing(unit, drawPos, iconSize, GameContext.PerspectiveFilter.IsSpectating);
	}

	private void DrawActivePings(Vector2 size)
	{
		foreach (var ping in GameContext.FXService.ActivePings)
		{
			Vector2 drawPos = MinimapHelper.WorldToMinimap(ping.WorldPos, size);

			float pulse = Mathf.Sin(ping.LifeTime * 15f) * 0.5f + 1.0f;
			float radius = 12f * pulse;

			DrawCircle(drawPos, radius, new Color(1f, 0.1f, 0.1f, 0.5f), false, 2.0f);
			DrawCircle(drawPos, radius - 4f, new Color(1f, 0.1f, 0.1f, 0.2f), true);
		}
	}
}