using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the agreement modal needs from the editor: the node it parents the overlay to, the shared option-button
/// styling, and the session flag that keeps the modal from reappearing on the next editor load. The member keeps the
/// wrapper's name because the localization generator only extracts keys from call sites naming a registered wrapper.
/// </summary>
internal sealed record AgreementDialogHooks(
	Node Owner,
	Action<Button, string, Action?, int, string> SetupOptionButton,
	Action<bool> SetShownThisSession);

/// <summary>
/// The Realm Creator Agreement that stands between the editor and a first publish. It is drawn over its own artwork
/// with fixed-position text boxes, so the layout below is positional rather than container-driven: the six rule boxes
/// sit where the artwork leaves room for them. Accepting records that the agreement was shown this session and runs
/// the continuation once the overlay has left the tree; quitting forgets that and returns to the main menu, which is
/// also what Escape does.
/// </summary>
internal sealed class AgreementDialog
{
	private readonly AgreementDialogHooks _hooks;

	public AgreementDialog(AgreementDialogHooks hooks)
	{
		_hooks = hooks;
	}

	public void Show(Action? onAccepted = null)
	{
		var overlay = new ColorRect();
		overlay.Name = "AgreementOverlay";
		overlay.Color = new Color(0, 0, 0, 0.75f);
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		overlay.MouseFilter = MouseFilterEnum.Stop;
		overlay.ZIndex = 1000;
		_hooks.Owner.AddChild(overlay);

		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = new PanelContainer();
		panel.Name = "Panel_PanelContainer";
		panel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
		panel.CustomMinimumSize = new Vector2(1024, 762);
		center.AddChild(panel);

		var bgTexRect = new TextureRect();
		bgTexRect.Name = "BgTexRect_TextureRect";
		bgTexRect.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/map_editor_agreement.webp");
		bgTexRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		bgTexRect.StretchMode = TextureRect.StretchModeEnum.Scale;
		bgTexRect.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddChild(bgTexRect);

		var contentOverlay = new Control();
		contentOverlay.Name = "ContentOverlay_Control";
		contentOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		panel.AddChild(contentOverlay);

		BuildBody(contentOverlay);

		var btnAccept = new Button();
		btnAccept.Name = "BtnAccept_Button";
		btnAccept.Set("icon_max_width", 0);
		_hooks.SetupOptionButton(btnAccept, "Accept", () =>
		{
			_hooks.SetShownThisSession(true);
			if (onAccepted != null)
			{
				overlay.TreeExited += () => onAccepted();
			}

			overlay.QueueFree();
		}, 13, "");
		btnAccept.Position = new Vector2(300, 692);
		btnAccept.Size = new Vector2(165, 42);
		contentOverlay.AddChild(btnAccept);

		var btnQuit = new Button();
		btnQuit.Name = "BtnQuit_Button";
		btnQuit.Set("icon_max_width", 0);
		_hooks.SetupOptionButton(btnQuit, "Quit", () =>
		{
			_hooks.SetShownThisSession(false);
			overlay.QueueFree();
			GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
		}, 13, "");
		btnQuit.Position = new Vector2(558, 692);
		btnQuit.Size = new Vector2(165, 42);
		contentOverlay.AddChild(btnQuit);
	}

	private static void BuildBody(Control contentOverlay)
	{
		var lblTitle = new Label();
		lblTitle.Name = "LblTitle_Label";
		UIStyle.ApplyTitle(lblTitle, TranslationServer.Translate("Realm Creator Agreement"), 20);
		lblTitle.Text = TranslationServer.Translate("Realm Creator Agreement").ToString().ToUpperInvariant();
		lblTitle.HorizontalAlignment = HorizontalAlignment.Center;
		lblTitle.Position = new Vector2(140, 48);
		lblTitle.Size = new Vector2(744, 30);
		contentOverlay.AddChild(lblTitle);

		AddIntro(contentOverlay, "LblIntro1_Label",
			TranslationServer.Translate("By publishing content on Realm, you grant us permission to host, distribute, and display your map so people can play it."),
			112);
		AddIntro(contentOverlay, "LblIntro2_Label",
			TranslationServer.Translate("We want Realm to be a thriving, collaborative arcade. Please respect these rules:"),
			132);

		AddRule(contentOverlay, "Your Work is Yours",
			"You retain full ownership of your original creations. You aren't signing away your copyright to anyone.",
			252, 185, 235, 125);

		AddRule(contentOverlay, "Monetization",
			"You may ask for donations from your player base, but you may not offer any differences in gameplay compared to non-paying users, other than cosmetic rewards. Pay-to-win is not allowed.",
			655, 185, 240, 125);

		AddRule(contentOverlay, "Collaboration",
			"By publishing your content on Realm, you allow other creators to open and learn from your work. You also grant them permission to adapt, build upon, and incorporate it into their own creations, provided those new works remain exclusively within the Realm platform.",
			252, 338, 235, 135);

		AddRule(contentOverlay, "Going Solo",
			"Want to turn your map into a standalone game? Go for it! However, you can only take your original work with you. You must replace any content created by other Realm users, unless you obtain their explicit written permission.",
			655, 338, 240, 135);

		AddRule(contentOverlay, "Give Credit",
			"If you import another creator's work, they still own the original. Never claim their work as your own.",
			252, 518, 235, 125);

		AddRule(contentOverlay, "No Plagiarism or Piracy",
			"Do not upload content you didn’t make or don't have the rights to use. This includes trademarked content from other video games, movies, music, and media.",
			655, 518, 240, 125);
	}

	private static void AddIntro(Control contentOverlay, string name, string text, float posY)
	{
		var lblIntro = new Label();
		lblIntro.Name = name;
		lblIntro.Text = text;
		lblIntro.HorizontalAlignment = HorizontalAlignment.Left;
		lblIntro.AddThemeFontSizeOverride("font_size", 12);
		lblIntro.AddThemeColorOverride("font_color", new Color(0.18f, 0.15f, 0.12f));
		lblIntro.Position = new Vector2(120, posY);
		lblIntro.Size = new Vector2(784, 20);
		contentOverlay.AddChild(lblIntro);
	}

	/// <summary>One numbered rule: a vermilion heading over wrapped body text, placed by hand over the artwork.</summary>
	private static void AddRule(Control contentOverlay, string title, string desc, float posX, float posY, float width, float height)
	{
		var textVBox = new VBoxContainer();
		textVBox.Name = "TextVBox_VBoxContainer";
		textVBox.Position = new Vector2(posX, posY);
		textVBox.Size = new Vector2(width, height);
		textVBox.AddThemeConstantOverride("separation", 2);

		var lblTitle = new Label();
		lblTitle.Name = "LblTitle_Label";
		lblTitle.Text = TranslationServer.Translate(title);
		lblTitle.AddThemeFontSizeOverride("font_size", 15);
		lblTitle.AddThemeColorOverride("font_color", new Color(0.72f, 0.35f, 0.12f));
		lblTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		textVBox.AddChild(lblTitle);

		var lblDesc = new Label();
		lblDesc.Name = "LblDesc_Label";
		lblDesc.Text = TranslationServer.Translate(desc);
		lblDesc.AddThemeFontSizeOverride("font_size", 11);
		lblDesc.AddThemeColorOverride("font_color", new Color(0.22f, 0.18f, 0.15f));
		lblDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lblDesc.CustomMinimumSize = new Vector2(width, 0);
		lblDesc.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		textVBox.AddChild(lblDesc);

		contentOverlay.AddChild(textVBox);
	}
}
