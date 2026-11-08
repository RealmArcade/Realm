using Godot;
using Realm.Client.Core;
using Realm.Client.Services;
using Realm.Client.Utils;
using System;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The four commands every selection has: move, stop, hold and build. They are styled and wired here because they
///     share one look and one notice; everything else on the command card is the selection's own business.
/// </summary>
public static class CommandCardButtons
{
	/// <summary>
	///     Styles and wires the four buttons found in the scene. <paramref name="feedback" /> shows the command's notice
	///     and <paramref name="enterBuildMenu" /> opens the build sub-menu.
	/// </summary>
	public static void Wire(Button move, Button stop, Button hold, Button build,
		Action<string, Color> feedback, Action enterBuildMenu)
	{
		Style(move, "res://Assets/UI/move_speed.webp", TranslationServer.Translate("[M] Move / Right-Click Ground"),
			() => GameContext.Host?.EnterCommandTargeting("move"));

		Style(stop, "res://Assets/UI/cancel_button_2.webp", TranslationServer.Translate("[S] Stop Selected Units"), () =>
		{
			feedback(TranslationServer.Translate("Command: Stop Current Action"), new Color(0.9f, 0.2f, 0.2f));
			GameContext.Host?.StopSelectedUnits();
		});

		Style(hold, "res://Assets/UI/magic_upgrade_arrow.webp",
			TranslationServer.Translate("[H] Hold Position — Unit stays put and attacks in place"), () =>
			{
				feedback(TranslationServer.Translate("Command: Hold Position"), new Color(0.9f, 0.8f, 0.1f));
				GameContext.Host?.HoldSelectedUnits();
			});

		Style(build, "res://Assets/UI/golden_hammers.webp", TranslationServer.Translate("[B] Build Structure"),
			enterBuildMenu);
	}

	/// <summary>
	///     The look shared by the console's icon buttons: a trimmed icon, a translated tooltip with the original as the
	///     fallback when a translation is missing, and a flat plate with no focus ring.
	/// </summary>
	public static void Style(Button btn, string iconPath, string tooltip, Action onClick)
	{
		btn.Flat = false;
		btn.Text = "";
		btn.ExpandIcon = true;
		btn.Icon = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
		string trans = TranslationServer.Translate(tooltip);
		btn.TooltipText = string.IsNullOrEmpty(trans) ? tooltip : trans;
		btn.CustomMinimumSize = new Vector2(48, 48);
		btn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		btn.FocusMode = Control.FocusModeEnum.None;
		UIStyle.SetupHUDButton(btn, tooltip);

		UIStyle.ApplyHudButtonStyleboxes(btn);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		btn.Pressed += () => onClick?.Invoke();
	}
}
