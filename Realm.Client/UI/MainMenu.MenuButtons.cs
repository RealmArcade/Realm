// The plain and textured button factories: text, icon, stylebox and text styling for the menu buttons,
// plus the texture-backed stylebox the main-menu column uses.

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
	private void SetupButton(Button button, string text, Action onClick)
	{
		button.Flat = false;
		button.Text = TranslationServer.Translate(text);
		button.AddThemeFontOverride("font", _norseBoldFont);
		button.AddThemeFontSizeOverride("font_size", 18);

		button.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		UIStyle.ApplyGoldHoverAndFocusColors(button);

		button.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#8C8171"));
		button.AddThemeConstantOverride("shadow_offset_x", 0);
		button.AddThemeConstantOverride("shadow_offset_y", 2);
		button.AddThemeConstantOverride("icon_max_width", 32);
		button.AddThemeConstantOverride("h_separation", 2);

		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);

		ApplyButtonAnimations(button, 1.03f);

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};
	}

	private void SetupMenuButton(Button button, string text, Action? onClick, string? texturePath = null, Vector2? minSize = null, string? iconPath = null)
	{
		button.Flat = false;
		button.Text = TranslationServer.Translate(text);
		button.CustomMinimumSize = minSize ?? (texturePath != null ? new Vector2(240, 59) : new Vector2(240, 40));

		ApplyMenuButtonIcon(button, iconPath);
		ApplyMenuButtonTextStyles(button);
		ApplyMenuButtonStyleBoxes(button, texturePath);

		button.Pressed += () =>
		{
			PlayClickSound();
			onClick?.Invoke();
		};

		button.MouseEntered += () => PlayHoverSound();
	}

	private void ApplyMenuButtonIcon(Button button, string? iconPath)
	{
		if (!string.IsNullOrEmpty(iconPath))
		{
			button.Icon = TrimmedTextureLoader.LoadTrimmedTexture(iconPath);
		}

		if (button.Icon == null) return;

		button.ExpandIcon = true;
		button.IconAlignment = HorizontalAlignment.Left;
		button.VerticalIconAlignment = VerticalAlignment.Center;
		button.AddThemeConstantOverride("icon_max_width", 24);
		button.AddThemeConstantOverride("h_separation", 10);
	}

	private void ApplyMenuButtonTextStyles(Button button)
	{
		button.AddThemeFontOverride("font", _norseBoldFont);
		button.AddThemeFontSizeOverride("font_size", 20);

		button.AddThemeColorOverride("font_color", Color.FromHtml("#D1C4AE"));
		UIStyle.ApplyGoldHoverAndFocusColors(button);

		button.AddThemeColorOverride("font_outline_color", new Color(0.10f, 0.07f, 0.02f, 1.0f));
		button.AddThemeConstantOverride("outline_size", 8);

		button.AddThemeColorOverride("font_shadow_color", Color.FromHtml("#8C8171"));
		button.AddThemeConstantOverride("shadow_offset_x", 0);
		button.AddThemeConstantOverride("shadow_offset_y", 2);
	}

	private void ApplyMenuButtonStyleBoxes(Button button, string? texturePath)
	{
		var normalStyle = CreateMenuButtonStyleBox(texturePath ?? "res://Assets/UI/menu_button_normal.webp", texturePath, null);
		var hoverStyle = CreateMenuButtonStyleBox(texturePath ?? "res://Assets/UI/menu_button_hover.webp", texturePath, new Color(1.12f, 1.10f, 0.96f));
		var pressedStyle = CreateMenuButtonStyleBox(texturePath ?? "res://Assets/UI/menu_button_pressed.webp", texturePath, new Color(0.85f, 0.82f, 0.75f));

		UIStyle.ApplyStyleBoxStates(button, normalStyle, hoverStyle, pressedStyle);
	}

	private StyleBoxTexture CreateMenuButtonStyleBox(string texPath, string? customPath, Color? modColor)
	{
		var style = new StyleBoxTexture();
		style.Texture = TrimmedTextureLoader.LoadTrimmedTexture(texPath);
		if (customPath != null && modColor.HasValue) style.ModulateColor = modColor.Value;

		if (customPath != null)
		{
			style.TextureMarginLeft = 0;
			style.TextureMarginRight = 0;
			style.TextureMarginTop = 0;
			style.TextureMarginBottom = 0;
			style.ContentMarginLeft = 24;
			style.ContentMarginRight = 24;
			style.ContentMarginTop = 6;
			style.ContentMarginBottom = 6;
		}
		else
		{
			style.TextureMarginLeft = 32;
			style.TextureMarginRight = 32;
			style.TextureMarginTop = 10;
			style.TextureMarginBottom = 10;
			style.ContentMarginLeft = 32;
			style.ContentMarginRight = 32;
			style.ContentMarginTop = 10;
			style.ContentMarginBottom = 10;
		}

		return style;
	}

}