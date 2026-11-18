using System;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI;

/// <summary>
///     The one confirmation a player reads before entering a lobby whose map nothing on Steam vouches for. It names the
///     reason rather than calling the map unsafe, because the map script is sandboxed either way and what the mark is about
///     is quality and maintenance. Leaving is always allowed, and nothing is hidden before this point: the lobby was listed
///     and the player chose it, so this is the last chance to say what they are walking into rather than a filter.
/// </summary>
[GlobalClass]
public partial class UntrustedMapConfirmDialog : Control
{
	private string _mapTitle = string.Empty;
	private MapTrustState _trust;
	private Action? _onConfirmed;

	/// <summary>Builds the dialog for one lobby's map. The caller adds it to the tree and it draws itself.</summary>
	/// <param name="mapTitle">The map's title, so the reason names the map rather than "this map".</param>
	/// <param name="trust">The mark the host stated for its map.</param>
	/// <param name="onConfirmed">What to do when the player accepts the warning.</param>
	public UntrustedMapConfirmDialog(string mapTitle, MapTrustState trust, Action onConfirmed)
	{
		Name = "UntrustedMapConfirmDialog";
		_mapTitle = mapTitle;
		_trust = trust;
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

		var body = new Label { Name = "UntrustedMapBody" };
		body.Text = string.Format(TranslationServer.Translate("{0} is not vouched for by Steam: {1}"),
			_mapTitle, MapTrustPolicy.DescribeReason(_trust.Reason));
		body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		body.CustomMinimumSize = new Vector2(460, 0);
		body.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		body.HorizontalAlignment = HorizontalAlignment.Center;
		body.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		body.AddThemeFontSizeOverride("font_size", 13);
		column.AddChild(body);

		var note = new Label { Name = "UntrustedMapNote" };
		note.Text = TranslationServer.Translate("The map's script runs in a sandbox either way; this is about how well the map is known, not about safety.");
		note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		note.CustomMinimumSize = new Vector2(460, 0);
		note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		note.HorizontalAlignment = HorizontalAlignment.Center;
		note.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		note.AddThemeFontSizeOverride("font_size", 12);
		column.AddChild(note);

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
