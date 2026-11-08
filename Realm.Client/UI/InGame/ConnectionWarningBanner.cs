using Godot;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The red banner across the top of the screen while the match has lost its host. It owns nothing but its own
///     appearance: whether it shows is the HUD's answer to the session's connection state.
/// </summary>
public static class ConnectionWarningBanner
{
	public static Label Create()
	{
		var banner = new Label();
		banner.Name = "ConnectionWarningLabel";
		banner.Text = TranslationServer.Translate("Connection to host lost ... Reconnecting");
		banner.HorizontalAlignment = HorizontalAlignment.Center;
		banner.VerticalAlignment = VerticalAlignment.Center;
		banner.AddThemeColorOverride("font_color", new Color(0.95f, 0.2f, 0.2f));
		banner.AddThemeFontSizeOverride("font_size", 22);

		var style = new StyleBoxFlat();
		style.BgColor = new Color(0.12f, 0.05f, 0.05f, 0.7f);
		style.BorderColor = new Color(0.8f, 0.2f, 0.2f, 0.5f);
		style.SetBorderWidthAll(2);
		style.ContentMarginLeft = 20;
		style.ContentMarginRight = 20;
		style.ContentMarginTop = 8;
		style.ContentMarginBottom = 8;
		banner.AddThemeStyleboxOverride("normal", style);

		// Anchored centre-top and grown both ways, so the label's own width decides how far it reaches.
		banner.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
		banner.GrowHorizontal = Control.GrowDirection.Both;
		banner.OffsetTop = 20;
		banner.OffsetLeft = -250;
		banner.OffsetRight = 250;
		banner.Visible = false;
		return banner;
	}
}
