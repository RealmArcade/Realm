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
	private void CreateDirectConnectInviteUI()
	{
		if (!Lobby.IsHost || Lobby.IsSinglePlayer || !Lobby.IsDirectLobby) return;

		var panel = new PanelContainer();
		panel.Name = "DirectConnectPanel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var vbox = new VBoxContainer();
		vbox.Name = "DirectConnectVBox";
		vbox.AddThemeConstantOverride("separation", 4);
		panel.AddChild(vbox);

		var title = new Label();
		title.Name = "DirectConnectTitle_Label";
		UIStyle.ApplyTitle(title, TranslationServer.Translate("DIRECT CONNECT"), 15);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		title.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(title);

		var hint = new Label();
		hint.Name = "DirectConnectHint_Label";
		hint.Text = TranslationServer.Translate("Send this address to the players you want to join. They open DIRECT CONNECT in the lobby browser and paste it.");
		hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		hint.CustomMinimumSize = new Vector2(560, 0);
		hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		hint.HorizontalAlignment = HorizontalAlignment.Center;
		hint.AddThemeFontSizeOverride("font_size", 12);
		hint.AddThemeColorOverride("font_color", new Color(0.85f, 0.87f, 0.92f));
		vbox.AddChild(hint);

		vbox.AddChild(CreateDirectConnectAddressRow("LanAddress", Lobby.DirectConnectAddress));

		if (Lobby.PublicConnectAddress is { Length: > 0 } publicAddress)
		{
			var publicHint = new Label();
			publicHint.Name = "PublicAddressHint_Label";
			publicHint.Text = TranslationServer.Translate("Players outside your network use this address when the router forwards the port:");
			publicHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			publicHint.CustomMinimumSize = new Vector2(560, 0);
			publicHint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			publicHint.HorizontalAlignment = HorizontalAlignment.Center;
			publicHint.AddThemeFontSizeOverride("font_size", 11);
			publicHint.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
			vbox.AddChild(publicHint);

			vbox.AddChild(CreateDirectConnectAddressRow("PublicAddress", publicAddress));
		}

		GetNode<VBoxContainer>("BriefingPanel/VBoxContainer").AddChild(panel);
	}

	private HBoxContainer CreateDirectConnectAddressRow(string namePrefix, string address)
	{
		var row = new HBoxContainer();
		row.Name = $"{namePrefix}Row";
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.AddThemeConstantOverride("separation", 8);

		var value = new Label();
		value.Name = $"{namePrefix}Label";
		value.Text = address;
		value.VerticalAlignment = VerticalAlignment.Center;
		value.CustomMinimumSize = new Vector2(340, 0);
		value.AddThemeFontSizeOverride("font_size", 17);
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

	private void CopyDirectConnectAddress(string address)
	{
		DisplayServer.ClipboardSet(address);
		GameContext.UserInterface?.PlayClickSound();
		ShowInviteFeedback(TranslationServer.Translate("Address copied."));
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

		var description = new Label();
		description.Name = "DirectConnectInviteDescription_Label";
		description.Text = Lobby.IsHost
			? TranslationServer.Translate("This lobby is not listed anywhere. Send one of these addresses to the players you want to play with.")
			: TranslationServer.Translate("This lobby is not listed anywhere. This is the address it is played at, and you can pass it on to another player.");
		description.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		description.CustomMinimumSize = new Vector2(600, 0);
		description.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		description.HorizontalAlignment = HorizontalAlignment.Center;
		description.AddThemeFontSizeOverride("font_size", 13);
		description.AddThemeColorOverride("font_color", new Color(0.85f, 0.87f, 0.92f));
		vbox.AddChild(description);

		if (Lobby.IsHost)
		{
			vbox.AddChild(CreateDirectConnectAddressRow("InviteLanAddress", Lobby.DirectConnectAddress));

			if (Lobby.PublicConnectAddress is { Length: > 0 } publicAddress)
			{
				var publicHint = new Label();
				publicHint.Name = "InvitePublicAddressHint_Label";
				publicHint.Text = TranslationServer.Translate("Players outside your network use this address when the router forwards the port:");
				publicHint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				publicHint.CustomMinimumSize = new Vector2(600, 0);
				publicHint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				publicHint.HorizontalAlignment = HorizontalAlignment.Center;
				publicHint.AddThemeFontSizeOverride("font_size", 12);
				publicHint.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
				vbox.AddChild(publicHint);

				vbox.AddChild(CreateDirectConnectAddressRow("InvitePublicAddress", publicAddress));
			}
		}
		else if (Lobby.JoinedLobbyAddress is { Length: > 0 } joinedAddress)
		{
			vbox.AddChild(CreateDirectConnectAddressRow("InviteHostAddress", joinedAddress));
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
