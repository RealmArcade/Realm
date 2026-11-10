using Godot;
using Realm.Client.UI;

namespace Realm.Client;

[GlobalClass]
public partial class FPSCounter : Label
{
	private float _updateTimer = 0f;
	private int _lastRenderedFps = -1;

	public override void _Ready()
	{
		AddThemeColorOverride("font_color", new Color(0.15f, 0.65f, 1.0f));
		AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.08f, 0.1f));
		AddThemeConstantOverride("outline_size", 4);
		AddThemeFontSizeOverride("font_size", 16);
		ZIndex = 100;
		Visible = GameSettings.DisplayFps;
	}

	public override void _Process(double delta)
	{
		if (Visible != GameSettings.DisplayFps)
		{
			Visible = GameSettings.DisplayFps;
		}

		if (Visible)
		{
			_updateTimer += (float)delta;
			if (_updateTimer >= 0.25f)
			{
				_updateTimer = 0f;
				int fps = (int)Engine.GetFramesPerSecond();
				if (fps != _lastRenderedFps)
				{
					_lastRenderedFps = fps;
					Text = string.Format(TranslationServer.Translate("FPS: {0}"), fps);
				}
			}
		}
	}
}