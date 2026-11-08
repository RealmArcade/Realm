// The hero button factories: play, avatar and icon buttons, each with its own container shape, plus the
// hover/press animations and the sound hooks they share.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Network;
using Realm.Shared;
using Realm.Shared.Network;
using HttpClient = System.Net.Http.HttpClient;
using static Godot.Control;
using static Godot.CanvasItem;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Accounts;


namespace Realm.Client.UI;

public partial class MainMenu
{
	private void SetupPlayButton(Button button, Action onClick)
	{
		button.Flat = false;
		button.Icon = null;
		button.CustomMinimumSize = new Vector2(240, 56);
		button.Text = TranslationServer.Translate("PLAY");

		button.AddThemeFontOverride("font", _norseBoldFont);
		button.AddThemeFontSizeOverride("font_size", 20);

		button.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		UIStyle.ApplyGoldHoverAndFocusColors(button);

		button.AddThemeColorOverride("font_outline_color", new Color(0.12f, 0.08f, 0.03f, 1.0f));
		button.AddThemeConstantOverride("outline_size", 8);

		button.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#8C8171"));
		button.AddThemeConstantOverride("shadow_offset_x", 0);
		button.AddThemeConstantOverride("shadow_offset_y", 2);

		UIStyle.ApplyTintedArtworkButton(button, "res://Assets/UI/menu_play_button.webp",
			new Color(1f, 1f, 1f), new Color(1.12f, 1.10f, 0.96f), new Color(0.85f, 0.82f, 0.75f),
			24, 24, 6, 6);
		button.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());

		ApplyButtonAnimations(button, 1.04f);

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void SetupAvatarButton(Button button, Action onClick)
	{
		button.Flat = true;
		button.Text = "";
		button.FocusMode = FocusModeEnum.None;

		UIStyle.ApplyPlainButtonStates(button);

		var frameNormal = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_frame_normal.webp");
		var nameplateNormal = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_nameplate_normal.webp");
		var backdropNormal = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/profile_backdrop_normal.webp");

		var backdrop = button.GetNode<TextureRect>("Backdrop");
		var avatar = button.GetNode<TextureRect>("Avatar");
		var frame = button.GetNode<TextureRect>("Frame");
		var nameplate = button.GetNode<TextureRect>("Nameplate");
		var nameLabel = button.GetNode<Label>("Nameplate/Label");
		var levelLabel = button.GetNode<Label>("Badge/Label");

		backdrop.Texture = backdropNormal;
		backdrop.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		backdrop.StretchMode = TextureRect.StretchModeEnum.Scale;
		backdrop.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		frame.Texture = frameNormal;
		frame.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		frame.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		frame.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		nameplate.Texture = nameplateNormal;
		nameplate.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		nameplate.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		nameplate.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		avatar.Texture = TrimmedTextureLoader.LoadTrimmedTexture("res://Assets/UI/avatar_icon.webp");
		avatar.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		avatar.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		avatar.TextureFilter = TextureFilterEnum.LinearWithMipmaps;

		ApplyProfileNameLabels();

		var labelFont = new SystemFont();
		labelFont.FontNames = new string[] { "Cinzel Bold", "Cinzel", "Palatino Linotype", "Georgia", "serif" };
		labelFont.FontWeight = 700;

		nameLabel.AddThemeFontOverride("font", labelFont);
		nameLabel.AddThemeFontSizeOverride("font_size", 14);
		nameLabel.AddThemeColorOverride("font_color", new Color(0.95f, 0.85f, 0.65f));
		nameLabel.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.05f, 1.0f));
		nameLabel.AddThemeConstantOverride("outline_size", 6);

		levelLabel.AddThemeFontOverride("font", labelFont);
		levelLabel.AddThemeFontSizeOverride("font_size", 12);
		levelLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.75f, 0.55f));
		levelLabel.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.05f, 1.0f));
		levelLabel.AddThemeConstantOverride("outline_size", 5);

		Tween? scaleTween = null;
		button.MouseEntered += () =>
		{
			PlayHoverSound();
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.SetParallel(true);
			button.PivotOffset = button.Size / 2;
			scaleTween.TweenProperty(button, "scale", new Vector2(1.04f, 1.04f), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			scaleTween.TweenProperty(button, "modulate", new Color(1.10f, 1.08f, 0.98f), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};

		button.MouseExited += () =>
		{
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.SetParallel(true);
			scaleTween.TweenProperty(button, "scale", Vector2.One, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			scaleTween.TweenProperty(button, "modulate", Colors.White, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void SetupIconButton(Button button, string text, string iconPath, Action onClick, Vector2? iconSize = null)
	{
		button.Flat = false;
		button.Icon = null;
		button.Text = "";

		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);

		foreach (Node child in button.GetChildren())
		{
			if (child.Name == "ButtonContent")
			{
				child.QueueFree();
			}
		}

		var container = CreateIconButtonContainer();

		var iconRect = CreateIconButtonIcon(iconPath, iconSize);
		container.AddChild(iconRect);

		var label = CreateIconButtonLabel(text);
		container.AddChild(label);

		button.AddChild(container);

		BindIconButtonEvents(button, label, iconRect, onClick);
	}

	private HBoxContainer CreateIconButtonContainer()
	{
		var container = new HBoxContainer();
		container.Name = "ButtonContent";
		container.AnchorLeft = 0.0f;
		container.AnchorRight = 1.0f;
		container.AnchorTop = 0.0f;
		container.AnchorBottom = 1.0f;
		container.Alignment = BoxContainer.AlignmentMode.Center;
		container.MouseFilter = MouseFilterEnum.Ignore;
		container.AddThemeConstantOverride("separation", 6);
		return container;
	}

	private TextureRect CreateIconButtonIcon(string iconPath, Vector2? iconSize)
	{
		var iconRect = new TextureRect();
		iconRect.Name = "IconRect_TextureRect";
		iconRect.Texture = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
		iconRect.TextureFilter = TextureFilterEnum.LinearWithMipmaps;
		iconRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		iconRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		Vector2 size = iconSize ?? new Vector2(22, 22);
		iconRect.CustomMinimumSize = size;
		iconRect.Size = size;
		iconRect.PivotOffset = size / 2.0f;
		iconRect.MouseFilter = MouseFilterEnum.Ignore;
		iconRect.Modulate = UIStyle.ColorGoldDull;
		return iconRect;
	}

	private Label CreateIconButtonLabel(string text)
	{
		var label = new Label();
		label.Name = "Label_Label";
		label.Text = TranslationServer.Translate(text);
		label.AddThemeFontOverride("font", _norseBoldFont);
		label.AddThemeFontSizeOverride("font_size", 18);
		label.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		label.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#8C8171"));
		label.AddThemeConstantOverride("shadow_offset_x", 0);
		label.AddThemeConstantOverride("shadow_offset_y", 2);
		label.MouseFilter = MouseFilterEnum.Ignore;
		return label;
	}

	private void BindIconButtonEvents(Button button, Label label, TextureRect iconRect, Action onClick)
	{
				ApplyButtonAnimations(button, 1.03f);

		button.MouseEntered += () =>
		{
			label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			iconRect.Modulate = UIStyle.ColorGold;
		};

		button.MouseExited += () =>
		{
			label.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
			iconRect.Modulate = UIStyle.ColorGoldDull;
		};

		button.ButtonDown += () =>
		{
			label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
			iconRect.Modulate = UIStyle.ColorCyanGlow;
		};

		button.ButtonUp += () =>
		{
			label.AddThemeColorOverride("font_color", button.IsHovered() ? UIStyle.ColorGold : Color.FromHtml("#D1C4AE"));
			iconRect.Modulate = button.IsHovered() ? UIStyle.ColorGold : UIStyle.ColorGoldDull;
		};

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void ApplyButtonAnimations(Button button, float hoverScale)
	{
		Tween? scaleTween = null;
		button.MouseEntered += () =>
		{
			PlayHoverSound();
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			button.PivotOffset = button.Size / 2;
			scaleTween.TweenProperty(button, "scale", new Vector2(hoverScale, hoverScale), 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};
		button.MouseExited += () =>
		{
			scaleTween?.Kill();
			scaleTween = button.CreateTween();
			scaleTween.TweenProperty(button, "scale", Vector2.One, 0.15f)
				.SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
		};
	}

}