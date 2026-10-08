using Godot;
using System;
using System.IO;
using Realm.Shared.Metadata;
using Realm.Shared.Distribution;
using Realm.Godot.Services;
using Realm.Godot.VFX;

public partial class AuthorSignatureDialog : FloatingDialogBase
{
	private LineEdit _txtUsername;
	private LineEdit _txtPublicKey;
	private Label _lblKeyPath;
	private Label _lblCopyStatus;

	public AuthorSignatureDialog(MapEditorHUD hud)
		: base(hud, TranslationServer.Translate("Author Signature"), new Vector2(540, 360))
	{
		SetUncompressedPanelTexture("res://Assets/UI/map_editor_author_signature.png", 42, 25, 55, 55);
		if (TitleLabel?.GetParent() is MarginContainer titleMargin)
		{
			titleMargin.AddThemeConstantOverride("margin_top", -42);
		}
		if (CloseButton?.GetParent() is MarginContainer closeMargin)
		{
			closeMargin.AddThemeConstantOverride("margin_top", -36);
			closeMargin.AddThemeConstantOverride("margin_right", -48);
		}
		BuildControls();
		SetFooterCloseOnly("CLOSE");
	}

	private void BuildControls()
	{
		var descMargin = new MarginContainer();
		descMargin.AddThemeConstantOverride("margin_top", 6);
		descMargin.AddThemeConstantOverride("margin_bottom", 6);
		descMargin.AddThemeConstantOverride("margin_left", 12);
		descMargin.AddThemeConstantOverride("margin_right", 12);
		descMargin.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var descLabel = new Label();
		descLabel.Text = TranslationServer.Translate("Your map publishing key is stored in authorship_key_DO-NOT-SHARE.rkey. It is used to sign all your maps and assets to verify your ownership so others can't overwrite your published files. Keep this key safely backed up in a secure location. NEVER share your private key or .rkey file. To collaborate on maps, share only your Public Key with team maintainers so they can authorize you to publish.");
		descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		descLabel.CustomMinimumSize = new Vector2(390, 0);
		descLabel.HorizontalAlignment = HorizontalAlignment.Center;
		descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		descLabel.AddThemeFontSizeOverride("font_size", 11);
		descLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.95f, 0.92f));
		descMargin.AddChild(descLabel);
		BodyContainer.AddChild(descMargin);

		var faFont = Hud?.GetFontAwesomeFont();

		var secHeader = AddSectionHeader(BodyContainer, "\uf2c2 " + TranslationServer.Translate("IDENTITY DETAILS"), UIStyle.ColorGold);
		if (faFont != null)
		{
			secHeader.AddThemeFontOverride("font", faFont);
		}

		var grid = new GridContainer();
		grid.Columns = 2;
		grid.AddThemeConstantOverride("h_separation", 10);
		grid.AddThemeConstantOverride("v_separation", 4);
		grid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		BodyContainer.AddChild(grid);

		var lblUser = new Label();
		lblUser.Text = TranslationServer.Translate("UserName:");
		lblUser.CustomMinimumSize = new Vector2(85, 0);
		lblUser.AddThemeFontSizeOverride("font_size", 10);
		lblUser.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		grid.AddChild(lblUser);

		_txtUsername = new LineEdit();
		_txtUsername.Editable = false;
		_txtUsername.CustomMinimumSize = new Vector2(0, 22);
		_txtUsername.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtUsername.AddThemeFontSizeOverride("font_size", 10);
		grid.AddChild(_txtUsername);

		var lblPub = new Label();
		lblPub.Text = TranslationServer.Translate("PublicKey:");
		lblPub.CustomMinimumSize = new Vector2(85, 0);
		lblPub.AddThemeFontSizeOverride("font_size", 10);
		lblPub.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		grid.AddChild(lblPub);

		var pubHBox = new HBoxContainer();
		pubHBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		pubHBox.AddThemeConstantOverride("separation", 6);

		_txtPublicKey = new LineEdit();
		_txtPublicKey.Editable = false;
		_txtPublicKey.CustomMinimumSize = new Vector2(0, 22);
		_txtPublicKey.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_txtPublicKey.AddThemeFontSizeOverride("font_size", 10);
		pubHBox.AddChild(_txtPublicKey);

		var btnCopyPub = AddButton(pubHBox, "\uf0c5 " + TranslationServer.Translate("Copy"), () => CopyPublicKeyToClipboard(), "Copy public key to clipboard", 10, new Vector2(65, 22));
		grid.AddChild(pubHBox);

		var lblPriv = new Label();
		lblPriv.Text = TranslationServer.Translate("Private Key:");
		lblPriv.CustomMinimumSize = new Vector2(85, 0);
		lblPriv.AddThemeFontSizeOverride("font_size", 10);
		lblPriv.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		grid.AddChild(lblPriv);

		var lblPrivVal = new Label();
		lblPrivVal.Text = "\uf023 " + TranslationServer.Translate("SECRET. DO NOT SHARE.");
		lblPrivVal.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		lblPrivVal.AddThemeFontSizeOverride("font_size", 10);
		lblPrivVal.AddThemeColorOverride("font_color", new Color(1.0f, 0.45f, 0.45f));
		if (faFont != null)
		{
			lblPrivVal.AddThemeFontOverride("font", faFont);
		}
		grid.AddChild(lblPrivVal);

		var lblPathTitle = new Label();
		lblPathTitle.Text = TranslationServer.Translate("File Location:");
		lblPathTitle.CustomMinimumSize = new Vector2(85, 0);
		lblPathTitle.AddThemeFontSizeOverride("font_size", 10);
		lblPathTitle.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		grid.AddChild(lblPathTitle);

		_lblKeyPath = new Label();
		_lblKeyPath.Text = string.Empty;
		_lblKeyPath.AddThemeFontSizeOverride("font_size", 10);
		_lblKeyPath.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_lblKeyPath.ClipText = true;
		_lblKeyPath.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_lblKeyPath.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		grid.AddChild(_lblKeyPath);

		var actionRow = new HBoxContainer();
		actionRow.AddThemeConstantOverride("separation", 10);
		actionRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		var btnOpenFolder = AddButton(actionRow, "\uf07c " + TranslationServer.Translate("Open Folder"), () => OpenKeysFolderInExplorer(), "Open key folder in Windows Explorer for backup", 10, new Vector2(110, 24));

		_lblCopyStatus = new Label();
		_lblCopyStatus.Text = string.Empty;
		_lblCopyStatus.AddThemeFontSizeOverride("font_size", 10);
		_lblCopyStatus.AddThemeColorOverride("font_color", new Color(0.4f, 0.9f, 0.4f));
		_lblCopyStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		actionRow.AddChild(_lblCopyStatus);

		BodyContainer.AddChild(actionRow);
	}

	private void CopyPublicKeyToClipboard()
	{
		string keyText = _txtPublicKey.Text.Trim();
		if (!string.IsNullOrEmpty(keyText))
		{
			DisplayServer.ClipboardSet(keyText);
			_lblCopyStatus.Text = TranslationServer.Translate("Public key copied to clipboard!");
			_lblCopyStatus.Modulate = new Color(1, 1, 1, 1);
			var tween = CreateTween();
			tween.TweenProperty(_lblCopyStatus, "modulate:a", 0.0f, 2.0f).SetDelay(1.5f);
		}
	}

	private void OpenKeysFolderInExplorer()
	{
		string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
		if (!Directory.Exists(keyDir))
		{
			Directory.CreateDirectory(keyDir);
		}

		string fullPath = Path.GetFullPath(keyDir);
		if (OperatingSystem.IsWindows())
		{
			try
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = $"\"{fullPath}\"",
					UseShellExecute = true
				});
				return;
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AuthorSignatureDialog] Failed to open explorer.exe: {ex.Message}");
			}
		}

		OS.ShellOpen(fullPath);
	}

	public override void OpenDialog()
	{
		RefreshKeyDetails();
		base.OpenDialog();
	}

	private void RefreshKeyDetails()
	{
		string keyDir = ProjectSettings.GlobalizePath("user://appdata/keys/");
		string defaultUsername = LobbyManager.Instance?.AuthenticatedUsername ?? string.Empty;
		var (key, data, keyPath, _) = AuthorshipKeyHelper.GetOrGenerateKeyInfo(keyDir, defaultUsername);

		_txtUsername.Text = !string.IsNullOrWhiteSpace(data.UserName) ? data.UserName : (string.IsNullOrWhiteSpace(defaultUsername) ? TranslationServer.Translate("(Not Registered)") : defaultUsername);
		_txtPublicKey.Text = data.PublicKey;
		_lblKeyPath.Text = keyPath;
		_lblCopyStatus.Text = string.Empty;
	}
}
