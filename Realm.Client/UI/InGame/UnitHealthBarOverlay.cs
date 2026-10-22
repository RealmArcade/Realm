using System.Collections.Generic;
using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.UI;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;

namespace Realm.Client.UI.InGame;

[GlobalClass]
public partial class UnitHealthBarOverlay : Control
{
	private readonly List<HealthBarEntry> _bars = new();
	private Camera3D _camera;

	private struct HealthBarEntry
	{
		public Vector2 Position;
		public float Width;
		public float Height;
		public float Fraction;
		public Color Color;
	}

	public void Refresh(Camera3D camera)
	{
		_camera = camera;
		_bars.Clear();

		var mode = GameSettings.ShowHealthBars;
		if (mode != HealthBarMode.Hidden && camera != null && GodotObject.IsInstanceValid(camera) && GameHost.Instance != null)
		{
			var world = GameContext.World;
			if (world != null)
			{
				Vector2 viewportSize = GetViewportRect().Size;
				var units = GameHost.Instance.AllUnits;

				for (int i = 0; i < units.Count; i++)
				{
					Unit3D unit = units[i];
					if (unit == null || !GodotObject.IsInstanceValid(unit) || unit.IsPreview || !unit.Visible) continue;
					if (unit.Entity == Realm.Ecs.Entity.Null || !world.IsAlive(unit.Entity)) continue;
					if (world.Has<Dead>(unit.Entity) || !world.Has<Health>(unit.Entity)) continue;
					if (world.Has<OverheadBarsHidden>(unit.Entity)) continue;

					ref Health health = ref world.Get<Health>(unit.Entity);
					if (health.Max <= 0f) continue;

					float fraction = Mathf.Clamp(health.Current / health.Max, 0f, 1f);
					if (mode == HealthBarMode.Damaged && fraction >= 1f) continue;

					ShroudService shroud = GameContext.ShroudService;
					if (shroud != null && !shroud.IsUnitVisibleOnMinimap(unit)) continue;

					float anchorHeight = Mathf.Max(2.2f, unit.GetBaseObstacleRadius() * 2.2f);
					Vector3 anchor = unit.GlobalPosition + new Vector3(0f, anchorHeight, 0f);
					if (camera.IsPositionBehind(anchor)) continue;

					Vector2 screen = camera.UnprojectPosition(anchor);
					if (screen.X < 0f || screen.Y < 0f || screen.X > viewportSize.X || screen.Y > viewportSize.Y) continue;

					float distance = camera.GlobalPosition.DistanceTo(anchor);
					float scale = Mathf.Clamp(14f / Mathf.Max(distance, 0.001f), 0.4f, 1.4f);

					_bars.Add(new HealthBarEntry
					{
						Position = screen,
						Width = 54f * scale,
						Height = 5f * scale,
						Fraction = fraction,
						Color = unit.PlayerColor
					});
				}
			}
		}

		QueueRedraw();
	}

	public override void _Draw()
	{
		for (int i = 0; i < _bars.Count; i++)
		{
			HealthBarEntry bar = _bars[i];
			float left = bar.Position.X - bar.Width * 0.5f;
			float top = bar.Position.Y - bar.Height;

			DrawRect(new Rect2(left - 1f, top - 1f, bar.Width + 2f, bar.Height + 2f), new Color(0f, 0f, 0f, 0.75f));
			DrawRect(new Rect2(left, top, bar.Width, bar.Height), new Color(0.12f, 0.12f, 0.12f, 0.85f));
			DrawRect(new Rect2(left, top, bar.Width * bar.Fraction, bar.Height), bar.Color);
		}
	}
}
