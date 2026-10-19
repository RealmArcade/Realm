using System;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Ecs.Components.Terrain;

namespace Realm.Client.UI;

[GlobalClass]
public partial class MinimapOverlay : Control
{
	private static Texture2D? _unitCircleTexture;
	private static Texture2D? _selectionRingTexture;
	private static Texture2D? _radarTexture;
	private static Texture2D? _pingTexture;
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

		var radarTex = GetRadarTexture();
		DrawTextureRect(radarTex, new Rect2(Vector2.Zero, size), false);
	}

	private void DrawShroudGrid(Vector2 size)
	{
		if (InGameHUD.Instance == null || string.Equals(InGameHUD.Instance.ShroudType, "visible", StringComparison.OrdinalIgnoreCase)) return;

		var shroudTex = GameContext.ShroudService?.ShroudMinimapTexture;
		if (shroudTex != null && GodotObject.IsInstanceValid(shroudTex))
		{
			DrawTextureRect(shroudTex, new Rect2(Vector2.Zero, size), false);
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

		float ringRadius = iconSize + 2.5f;
		var ringRect = new Rect2(drawPos.X - ringRadius, drawPos.Y - ringRadius, ringRadius * 2f, ringRadius * 2f);
		DrawTextureRect(GetSelectionRingTexture(), ringRect, false, selColor);
	}

	private void DrawSelectionRing(Unit3D unit, Vector2 drawPos, float iconSize)
	{
		DrawSelectionRing(unit, drawPos, iconSize, GameContext.PerspectiveFilter.IsSpectating);
	}

	private void DrawActivePings(Vector2 size)
	{
		var pingTex = GetPingTexture();
		foreach (var ping in GameContext.FXService.ActivePings)
		{
			Vector2 drawPos = MinimapHelper.WorldToMinimap(ping.WorldPos, size);

			float pulse = Mathf.Sin(ping.LifeTime * 15f) * 0.5f + 1.0f;
			float radius = 12f * pulse;

			var pingRect = new Rect2(drawPos.X - radius, drawPos.Y - radius, radius * 2f, radius * 2f);
			DrawTextureRect(pingTex, pingRect, false, new Color(1f, 0.1f, 0.1f, 0.8f));
		}
	}

	public static Texture2D GetSelectionRingTexture()
	{
		if (_selectionRingTexture != null && GodotObject.IsInstanceValid(_selectionRingTexture))
		{
			return _selectionRingTexture;
		}

		const int textureSize = 64;
		const float center = textureSize * 0.5f;
		const float outerRadius = 30f;
		const float strokeWidth = 5f;
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

				if (dist >= innerRadius - antialiasMargin && dist <= outerRadius + antialiasMargin)
				{
					float alpha = 1f;
					if (dist < innerRadius + antialiasMargin)
					{
						alpha = (dist - (innerRadius - antialiasMargin)) / (antialiasMargin * 2f);
					}
					else if (dist > outerRadius - antialiasMargin)
					{
						alpha = 1f - (dist - (outerRadius - antialiasMargin)) / (antialiasMargin * 2f);
					}

					image.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
				}
				else
				{
					image.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
				}
			}
		}

		image.GenerateMipmaps();
		_selectionRingTexture = ImageTexture.CreateFromImage(image);
		return _selectionRingTexture;
	}

	public static Texture2D GetRadarTexture()
	{
		if (_radarTexture != null && GodotObject.IsInstanceValid(_radarTexture))
		{
			return _radarTexture;
		}

		const int textureSize = 256;
		const float center = textureSize * 0.5f;
		Color bgColor = new Color(0.04f, 0.08f, 0.04f, 1.0f);
		Color radarGridRawColor = new Color(0.1f, 0.4f, 0.15f, 0.3f);
		Color blendedGridColor = bgColor.Blend(radarGridRawColor);
		Color bronzeColor = UIStyle.ColorBronze;

		var image = Image.CreateEmpty(textureSize, textureSize, false, Image.Format.Rgba8);
		image.Fill(bgColor);

		float r1 = textureSize * 0.45f;
		float r2 = textureSize * 0.3f;
		float r3 = textureSize * 0.15f;

		for (int y = 0; y < textureSize; y++)
		{
			for (int x = 0; x < textureSize; x++)
			{
				if (x == 0 || x == textureSize - 1 || y == 0 || y == textureSize - 1)
				{
					image.SetPixel(x, y, bronzeColor);
					continue;
				}

				if (x == textureSize / 2 || y == textureSize / 2)
				{
					image.SetPixel(x, y, blendedGridColor);
					continue;
				}

				float deltaX = x + 0.5f - center;
				float deltaY = y + 0.5f - center;
				float dist = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);

				if (Mathf.Abs(dist - r1) <= 1.0f || Mathf.Abs(dist - r2) <= 0.75f || Mathf.Abs(dist - r3) <= 0.75f)
				{
					image.SetPixel(x, y, blendedGridColor);
				}
			}
		}

		_radarTexture = ImageTexture.CreateFromImage(image);
		return _radarTexture;
	}

	public static Texture2D GetPingTexture()
	{
		if (_pingTexture != null && GodotObject.IsInstanceValid(_pingTexture))
		{
			return _pingTexture;
		}

		const int textureSize = 64;
		const float center = textureSize * 0.5f;
		const float outerRadius = 30f;
		const float ringWidth = 5f;
		const float innerRingRadius = outerRadius - ringWidth;

		var image = Image.CreateEmpty(textureSize, textureSize, false, Image.Format.Rgba8);
		for (int y = 0; y < textureSize; y++)
		{
			for (int x = 0; x < textureSize; x++)
			{
				float deltaX = x + 0.5f - center;
				float deltaY = y + 0.5f - center;
				float dist = Mathf.Sqrt(deltaX * deltaX + deltaY * deltaY);

				if (dist > outerRadius)
				{
					image.SetPixel(x, y, new Color(0f, 0f, 0f, 0f));
				}
				else if (dist >= innerRingRadius)
				{
					image.SetPixel(x, y, new Color(1f, 1f, 1f, 0.6f));
				}
				else
				{
					float fillFactor = 1f - (dist / innerRingRadius);
					image.SetPixel(x, y, new Color(1f, 1f, 1f, 0.25f * fillFactor));
				}
			}
		}

		image.GenerateMipmaps();
		_pingTexture = ImageTexture.CreateFromImage(image);
		return _pingTexture;
	}
}