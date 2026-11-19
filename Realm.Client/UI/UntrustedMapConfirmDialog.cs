using System;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI;

/// <summary>
///     The one confirmation a player reads before entering a lobby whose map Steam does not vouch for. It says what an
///     unvouched map can mean - a map still being tested, a modified copy of an official one, or a host in a country where
///     Steam Workshop is blocked - because the map script is sandboxed either way and the warning is about where the map
///     came from, not about safety. Leaving is always allowed, and nothing is hidden before this point: the lobby was
///     listed and the player chose it, so this is the last chance to say what they are walking into rather than a filter.
/// </summary>
[GlobalClass]
public partial class UntrustedMapConfirmDialog : Control
{
	private Action? _onConfirmed;

	/// <summary>Builds the dialog for one lobby's map. The caller adds it to the tree and it draws itself.</summary>
	/// <param name="onConfirmed">What to do when the player accepts the warning.</param>
	public UntrustedMapConfirmDialog(Action onConfirmed)
	{
		Name = "UntrustedMapConfirmDialog";
		_onConfirmed = onConfirmed;
	}

	public override void _Ready()
	{
		SetAnchorsPreset(LayoutPreset.FullRect);

		var backdrop = new ColorRect { Name = "UntrustedMapBackdrop", Color = new Color(0, 0, 0, 0.6f) };
		backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		AddChild(backdrop);

		var center = new CenterContainer { Name = "UntrustedMapCenter" };
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var card = new PanelContainer { Name = "UntrustedMapCard" };
		card.CustomMinimumSize = new Vector2(520, 0);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateArtworkPlate(
			"res://Assets/UI/player_profile_card_bg.webp", 24, 24, 24, 24, 40));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "UntrustedMapMargin" };
		margin.AddThemeConstantOverride("margin_left", 26);
		margin.AddThemeConstantOverride("margin_right", 26);
		margin.AddThemeConstantOverride("margin_top", 22);
		margin.AddThemeConstantOverride("margin_bottom", 22);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "UntrustedMapColumn" };
		column.AddThemeConstantOverride("separation", 10);
		margin.AddChild(column);

		var title = new Label { Name = "UntrustedMapTitle" };
		title.Text = TranslationServer.Translate("THIS MAP IS NOT VERIFIED");
		UIStyle.ApplyTitle(title, title.Text, 20);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(title);

		var warning = new Label { Name = "UntrustedMapWarning" };
		warning.Text = TranslationServer.Translate("Warning: This host chose a map file outside of Steam. Either this is an unfinished map requesting beta testers, or it is a modified version of an official map, or the host is in a country which blocks access to Steam.");
		warning.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		warning.CustomMinimumSize = new Vector2(460, 0);
		warning.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		warning.HorizontalAlignment = HorizontalAlignment.Center;
		warning.AddThemeColorOverride("font_color", Colors.White);
		warning.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.07f, 0.9f));
		warning.AddThemeConstantOverride("outline_size", 4);
		warning.AddThemeFontSizeOverride("font_size", 13);
		column.AddChild(warning);

		var row = new HBoxContainer { Name = "UntrustedMapButtons" };
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.AddThemeConstantOverride("separation", 10);
		column.AddChild(row);

		Button join = CreateButton(row, "UntrustedMapJoinButton", "JOIN ANYWAY");
		join.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			Action? confirmed = _onConfirmed;
			_onConfirmed = null;
			QueueFree();
			confirmed?.Invoke();
		};

		Button cancel = CreateButton(row, "UntrustedMapCancelButton", "CANCEL");
		cancel.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			QueueFree();
		};
	}

	private static Button CreateButton(Node parent, string name, string textKey)
	{
		var button = new Button { Name = name };
		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);
		UIStyle.ApplyButtonText(button, TranslationServer.Translate(textKey), 14);
		button.CustomMinimumSize = new Vector2(180, 42);
		parent.AddChild(button);
		return button;
	}
}
