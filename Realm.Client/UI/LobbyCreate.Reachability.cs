using System.Collections.Generic;
using Godot;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Shared.Reachability;

namespace Realm.Client.UI;

/// <summary>
///     The screen a host sees when a lobby could not be shown to be reachable. It names what to change on this computer and
///     on the router, says that opening the port on the computer alone is usually enough because the lobby server punches a
///     way through the router, and points at Direct Connect for players who share a network with the host. Offering the
///     instructions is the whole point of refusing to publish the lobby: a lobby nobody can reach is otherwise only
///     discovered by the players who waited for it to time out.
/// </summary>
public partial class LobbyCreate
{
	private void ShowReachabilityHelpModal(HostReachabilityResult failure)
	{
		GameContext.UserInterface?.PlayWarningSound();

		var overlay = new ColorRect();
		overlay.Name = "ReachabilityHelpOverlay";
		overlay.Color = new Color(0, 0, 0, 0.55f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.ZIndex = 1200;
		AddChild(overlay);

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "ReachabilityHelpPanel";
		panel.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		panel.CustomMinimumSize = new Vector2(680, 0);
		panel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		center.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.Name = "ReachabilityHelpVBox";
		vbox.AddThemeConstantOverride("separation", 14);
		panel.AddChild(vbox);

		var title = new Label();
		title.Name = "ReachabilityHelpTitle_Label";
		UIStyle.ApplyTitle(title, Tr("COULD NOT PUBLISH THE LOBBY"), 20);
		title.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		title.HorizontalAlignment = HorizontalAlignment.Center;
		vbox.AddChild(title);

		vbox.AddChild(CreateReachabilityHelpScroller(failure));

		var buttons = new HBoxContainer();
		buttons.Name = "ReachabilityHelpButtons_HBoxContainer";
		buttons.Alignment = BoxContainer.AlignmentMode.Center;
		buttons.AddThemeConstantOverride("separation", 20);
		vbox.AddChild(buttons);

		var retryButton = UIStyle.CreateActionButton("ReachabilityHelpRetry_Button", Tr("TRY AGAIN"), 14);
		retryButton.CustomMinimumSize = new Vector2(170, 40);
		retryButton.AddThemeConstantOverride("icon_max_width", 0);
		retryButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			overlay.QueueFree();
			OnCreatePressed();
		};
		buttons.AddChild(retryButton);

		var closeButton = UIStyle.CreateActionButton("ReachabilityHelpClose_Button", Tr("CLOSE"), 14);
		closeButton.CustomMinimumSize = new Vector2(170, 40);
		closeButton.AddThemeConstantOverride("icon_max_width", 0);
		closeButton.Pressed += () =>
		{
			GameContext.UserInterface?.PlayClickSound();
			overlay.QueueFree();
		};
		buttons.AddChild(closeButton);
	}

	private Control CreateReachabilityHelpScroller(HostReachabilityResult failure)
	{
		var scroller = new ScrollContainer();
		scroller.Name = "ReachabilityHelpScroller";
		scroller.CustomMinimumSize = new Vector2(640, 330);
		scroller.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroller.SizeFlagsVertical = SizeFlags.ExpandFill;
		scroller.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;

		var paragraphs = new VBoxContainer();
		paragraphs.Name = "ReachabilityHelpParagraphs_VBoxContainer";
		paragraphs.AddThemeConstantOverride("separation", 12);
		paragraphs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		scroller.AddChild(paragraphs);

		foreach (string paragraph in DescribeReachabilityHelp(failure))
		{
			var label = new Label();
			label.Name = "ReachabilityHelpParagraph_Label";
			label.Text = paragraph;
			label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			label.CustomMinimumSize = new Vector2(620, 0);
			label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			label.AddThemeFontSizeOverride("font_size", 14);
			label.AddThemeColorOverride("font_color", new Color(0.9f, 0.91f, 0.95f));
			paragraphs.AddChild(label);
		}

		return scroller;
	}

	private List<string> DescribeReachabilityHelp(HostReachabilityResult failure)
	{
		int port = LobbyForHelp()?.LobbyPort ?? 0;
		string directConnectEndpoint = $"{LobbyManager.GetLocalIPAddress()}:{(port > 0 ? port : 8999)}";

		if (failure.Failure is HostReachabilityFailure.LoopbackUnanswered or HostReachabilityFailure.LoopbackNotOnLocalAddress)
		{
			return new List<string>
			{
				Tr("Your own game could not reach the lobby on this computer, so the lobby was not published. This is a local connection and has nothing to do with your router."),
				Tr("The host plays its own seat over that local connection, so without it nobody could control their own player."),
				Tr("Security software that filters local connections can block it, and so can a program that has taken the port. Allow Realm through it, then host again.")
			};
		}

		if (failure.Failure is HostReachabilityFailure.RegistryUnreachable or HostReachabilityFailure.ProbeSocketUnknown)
		{
			return new List<string>
			{
				Tr("The lobby server could not be reached, so this game could not confirm that other players can reach you. The lobby was not published."),
				Tr("Check your internet connection and try again. A network that blocks the lobby server also blocks the players who would join you."),
				string.Format(Tr("Players on your own network can still join you with Direct Connect at {0}."), directConnectEndpoint)
			};
		}

		string firewallParagraph = LobbyForHelp()?.LastFirewallGrant.Grant == WindowsFirewallGrant.Declined
			? Tr("You dismissed the Windows firewall prompt, so the port is still closed. Host again and approve it, or allow Realm through Windows Defender Firewall in Windows Security.")
			: string.Format(Tr("WINDOWS FIREWALL: allow Realm to receive incoming UDP connections on port {0}. The game asks Windows for this each time you host, so approve the prompt if it appears. If you dismissed it before, hosting asks again."), port);

		return new List<string>
		{
			string.Format(Tr("Other players could not reach this computer on UDP port {0}, so the lobby was not published."), port),
			firewallParagraph,
			string.Format(Tr("ROUTER: forward UDP port {0} to this computer, or switch on UPnP in the router's settings."), port),
			Tr("You only need one of these. Opening the port on this computer is often enough, because the lobby server helps other players punch through the router when they join."),
			string.Format(Tr("DIRECT CONNECT: players on your own network can join with Direct Connect at {0}, even when no port is open."), directConnectEndpoint),
			Tr("If neither your firewall nor your router can be changed, join another player's lobby instead. Hosting only works when other players can reach you.")
		};
	}

	private static LobbyManager? LobbyForHelp() => GameContext.Lobby;
}
