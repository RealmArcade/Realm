using System;
using Godot;
using Realm.Client.Services;
using static Godot.Control;

namespace Realm.Client.UI;

/// <summary>
///     The address half of a lobby that is not listed anywhere. The host reads the address here and sends it to the
///     players who should join, rather than inviting them through a lobby server that does not know the lobby exists.
/// </summary>
public partial class LobbyRoom
{
	/// <summary>
	///     Writes the addresses this lobby is played at, and what a host has to know about each: the address on this
	///     network, the address a player anywhere reaches when a router forward was measured, and - when none was - the
	///     sentence that stops a host sending that address to somebody it cannot work for.
	/// </summary>
	/// <param name="container">The column the addresses are written into.</param>
	/// <param name="prefix">The name each address row and label is given.</param>
	/// <param name="width">The width the wrapping text under each address may wrap in.</param>
	/// <param name="addressFontSize">The size the address itself is drawn at.</param>
	private void AddDirectConnectAddresses(VBoxContainer container, string prefix, int width, int addressFontSize)
	{
		container.AddChild(CreateDirectConnectAddressRow(prefix, Lobby.DirectConnectAddress, width, addressFontSize));

		if (Lobby.PublicConnectAddress is { Length: > 0 } publicAddress)
		{
			container.AddChild(CreateDirectConnectNote(
				$"{prefix}PublicHint_Label",
				"Players outside your network join at this address:",
				width,
				11,
				UIStyle.ColorGoldDull));
			container.AddChild(CreateDirectConnectAddressRow($"{prefix}Public", publicAddress, width, addressFontSize));
			return;
		}

		container.AddChild(CreateDirectConnectNote(
			$"{prefix}NoForwardHint_Label",
			"Nothing outside your network has reached UDP port {0}, so the address above only works on your own network. Forward that port on your router, or have your friends join through a virtual LAN such as Tailscale, to play with them anywhere.",
			width,
			11,
			UIStyle.ColorGoldDull,
			Lobby.BoundPort));

		// The router almost always maps the port to something, so the address outside players would use is worth showing even
		// while nothing confirmed it. It is offered as a second address with the condition attached, because a host who has
		// forwarded the port by hand is the one case a direct lobby cannot measure for itself and the one it cannot work for.
		if (Lobby.MeasuredPublicConnectAddress is not { Length: > 0 } measuredAddress) return;

		container.AddChild(CreateDirectConnectNote(
			$"{prefix}MeasuredPublicHint_Label",
			"With UDP port {0} forwarded on your router, players outside your network use this address instead:",
			width,
			11,
			UIStyle.ColorGoldDull,
			Lobby.BoundPort));
		container.AddChild(CreateDirectConnectAddressRow($"{prefix}MeasuredPublic", measuredAddress, width, addressFontSize));
	}

	/// <summary>
	///     Writes a note whose sentence is a translation key with the values that go inside it, so a note built around the
	///     port a router has to forward is redrawn in the new language rather than left in the one it was written in.
	/// </summary>
	/// <param name="name">The name the label is given.</param>
	/// <param name="key">The translation key, which is also the English sentence.</param>
	/// <param name="width">The width the sentence may wrap in.</param>
	/// <param name="fontSize">The size the sentence is drawn at.</param>
	/// <param name="color">The colour the sentence is drawn in.</param>
	/// <param name="arguments">The values the sentence is formatted with.</param>
	private Label CreateDirectConnectNote(string name, string key, int width, int fontSize, Color color, params object?[] arguments)
	{
		Label note = CreateDirectConnectNoteFromText(name, string.Empty, width, fontSize, color);
		LocalizedText.Set(note, key, arguments);
		return note;
	}

	private Label CreateDirectConnectNoteFromText(string name, string text, int width, int fontSize, Color color)
	{
		var note = new Label();
		note.Name = name;
		note.Text = text;
		note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		note.CustomMinimumSize = new Vector2(width, 0);
		note.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		note.HorizontalAlignment = HorizontalAlignment.Center;
		note.AddThemeFontSizeOverride("font_size", fontSize);
		note.AddThemeColorOverride("font_color", color);
		return note;
	}

	private HBoxContainer CreateDirectConnectAddressRow(string namePrefix, string address, int width, int fontSize)
	{
		var row = new HBoxContainer();
		row.Name = $"{namePrefix}Row";
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.AddThemeConstantOverride("separation", 8);

		var value = new Label();
		value.Name = $"{namePrefix}Label";
		value.Text = address;
		value.VerticalAlignment = VerticalAlignment.Center;
		value.CustomMinimumSize = new Vector2(width - 160, 0);
		value.AddThemeFontSizeOverride("font_size", fontSize);
		value.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		row.AddChild(value);

		var copyButton = UIStyle.CreateActionButton($"{namePrefix}Copy_Button", TranslationServer.Translate("COPY"), 13);
		copyButton.CustomMinimumSize = new Vector2(110, 32);
		copyButton.AddThemeConstantOverride("icon_max_width", 0);
		copyButton.TooltipText = TranslationServer.Translate("Copy this address so you can send it to your friends.");
		copyButton.Pressed += () => CopyDirectConnectAddress(address);
		row.AddChild(copyButton);

		return row;
	}

	/// <summary>
	///     Offers the whole message a host sends, rather than the address alone: a friend who is handed a bare address has to
	///     be told what it is for and where it goes, and that is one copy rather than two plus an explanation typed out.
	/// </summary>
	/// <param name="container">The column the button is written into.</param>
	private void AddDirectConnectInviteCopyButton(VBoxContainer container)
	{
		var copyInviteButton = UIStyle.CreateActionButton("CopyInviteMessage_Button", TranslationServer.Translate("COPY INVITE MESSAGE"), 13);
		copyInviteButton.CustomMinimumSize = new Vector2(240, 34);
		copyInviteButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		copyInviteButton.AddThemeConstantOverride("icon_max_width", 0);
		copyInviteButton.TooltipText = TranslationServer.Translate("Copies the map name and the address to join at as one message you can paste into a chat. It uses the address players outside your network reach when that was confirmed, and your own network's address otherwise.");
		copyInviteButton.Pressed += CopyDirectConnectInviteMessage;
		container.AddChild(copyInviteButton);
	}

	private void CopyDirectConnectAddress(string address)
	{
		DisplayServer.ClipboardSet(address);
		GameContext.UserInterface?.PlayClickSound();
		ShowInviteFeedback(TranslationServer.Translate("Address copied."));
	}

	private void CopyDirectConnectInviteMessage()
	{
		string message = Lobby.DirectConnectInviteMessage;
		if (message.Length == 0) return;

		DisplayServer.ClipboardSet(message);
		GameContext.UserInterface?.PlayClickSound();
		ShowInviteFeedback(TranslationServer.Translate("Invite message copied."));
	}

	private void ShowDirectConnectInviteDialog()
	{
		var overlay = new ColorRect();
		overlay.Name = "DirectConnectInviteOverlay";
		overlay.Color = new Color(0, 0, 0, 0.55f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.ZIndex = 1200;
		AddChild(overlay);

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "DirectConnectInvitePanel";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(640, 0);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "DirectConnectInviteVBox";
		vbox.AddThemeConstantOverride("separation", 12);
		panel.AddChild(vbox);

		var title = new Label();
		title.Name = "DirectConnectInviteTitle_Label";
		UIStyle.ApplyTitle(title, TranslationServer.Translate("DIRECT CONNECT"), 22);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(title);

		vbox.AddChild(CreateDirectConnectNoteFromText(
			"DirectConnectInviteDescription_Label",
			Lobby.IsHost
				? TranslationServer.Translate("This lobby is not listed anywhere. Send one of these addresses to the players you want to play with.")
				: TranslationServer.Translate("This lobby is not listed anywhere. This is the address it is played at, and you can pass it on to another player."),
			600,
			13,
			new Color(0.85f, 0.87f, 0.92f)));

		if (Lobby.IsHost)
		{
			AddDirectConnectAddresses(vbox, "InviteLan", 600, 17);
			AddDirectConnectInviteCopyButton(vbox);
		}
		else if (Lobby.JoinedLobbyAddress is { Length: > 0 } joinedAddress)
		{
			vbox.AddChild(CreateDirectConnectAddressRow("InviteHostAddress", joinedAddress, 600, 17));
		}

		var closeButton = UIStyle.CreateActionButton("DirectConnectInviteClose_Button", TranslationServer.Translate("CLOSE"), 14);
		closeButton.CustomMinimumSize = new Vector2(170, 40);
		closeButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		closeButton.AddThemeConstantOverride("icon_max_width", 0);
		closeButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			overlay.QueueFree();
		};
		vbox.AddChild(closeButton);
	}
}
