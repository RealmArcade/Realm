using System;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using NSec.Cryptography;
using Realm.Client.Core;
using Key = NSec.Cryptography.Key;
using Realm.Client.Services;
using static Godot.Control;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the registration prompt needs from the editor: the node it parents the overlay to, the option-button styling,
/// and the feedback line. The member keeps the wrapper's name because the localization generator only extracts keys
/// from call sites naming a registered wrapper.
/// </summary>
internal sealed record CreatorRegistrationHooks(
	Node Owner,
	Action<Button, string, Action?, int, string> SetupOptionButton,
	Action<string> ShowFeedback);

/// <summary>
/// The prompt that offers to register a freshly generated map key under the signed-in account. The key is only
/// prompted for once per key: the authority is asked first, and the dialog is skipped when the check says the key is
/// already registered, when the account is not signed in, or when another overlay is already up.
/// </summary>
internal sealed class CreatorRegistrationDialog
{
	private const string OverlayName = "CreatorRegistrationOverlay";
	private const string AgreementOverlayName = "AgreementOverlay";

	private readonly CreatorRegistrationHooks _hooks;

	public CreatorRegistrationDialog(CreatorRegistrationHooks hooks)
	{
		_hooks = hooks;
	}

	public async void CheckAsync(Key key)
	{
		try
		{
			await PromptAsync(key);
		}
		catch (Exception exception)
		{
			// Publishing a map calls this before it asks the authority whether the key is registered. Without a catch,
			// a fault here vanished and the author was simply never prompted, with the publish path looking fine.
			GameLog.LogError($"[MapEditorHUD] Checking creator registration failed: {exception}");
		}
	}

	private async Task PromptAsync(Key key)
	{
		if (HasOverlayOpen(AgreementOverlayName) || HasOverlayOpen(OverlayName))
		{
			return;
		}

		string pubKeyStr = Convert.ToBase64String(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

		string accountAuthorityUrl = AccountService.AuthorityUrl;

		await GameContext.NetworkService.CheckCreatorRegistrationAsync(pubKeyStr, accountAuthorityUrl, (keyStr) => { TryShow(keyStr, key); });
	}

	private bool HasOverlayOpen(string nodeName) => _hooks.Owner.GetNodeOrNull(nodeName) != null;

	private void TryShow(string pubKeyStr, Key key)
	{
		// The authority answers asynchronously, so the editor may have been torn down or may have opened another
		// overlay while the check was in flight.
		if (GodotObject.IsInstanceValid(_hooks.Owner) && _hooks.Owner.IsInsideTree() && !HasOverlayOpen(OverlayName) && !HasOverlayOpen(AgreementOverlayName))
		{
			Show(pubKeyStr, key);
		}
	}

	private void Show(string pubKeyStr, Key key)
	{
		if (HasOverlayOpen(OverlayName)) return;

		string accountUserName = GameContext.Account.DisplayName;
		if (!GameContext.Account.IsSignedIn || string.IsNullOrWhiteSpace(accountUserName))
		{
			_hooks.ShowFeedback(TranslationServer.Translate("Sign in with Steam before registering a map key."));
			return;
		}

		var overlay = CreateOverlay();
		var vbox = CreateContainer(overlay);

		var lineEdit = new LineEdit { PlaceholderText = accountUserName, Alignment = HorizontalAlignment.Center, Text = accountUserName, Editable = false };
		UIStyle.ApplyTextInput(lineEdit);
		vbox.AddChild(lineEdit);

		var errLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		errLabel.AddThemeColorOverride("font_color", new Color(1, 0.3f, 0.3f));
		errLabel.AddThemeFontSizeOverride("font_size", 11);
		vbox.AddChild(errLabel);

		var btnRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		btnRow.AddThemeConstantOverride("separation", 12);

		var btnCancel = UIStyle.CreateBareButton("BtnCancel_Button");
		_hooks.SetupOptionButton(btnCancel, TranslationServer.Translate("Cancel / Skip"), () => overlay.QueueFree(), 13, "");
		btnCancel.CustomMinimumSize = new Vector2(130, 36);
		btnRow.AddChild(btnCancel);

		var btnRegister = new Button();
		btnRegister.Name = "BtnRegister_Button";
		btnRegister.Set("icon_max_width", 0);
		_hooks.SetupOptionButton(btnRegister, TranslationServer.Translate("Register"), null, 13, "");
		btnRegister.CustomMinimumSize = new Vector2(150, 36);
		btnRegister.Pressed += () => OnRegisterPressed(lineEdit, errLabel, btnRegister, overlay, pubKeyStr, key);
		btnRow.AddChild(btnRegister);

		vbox.AddChild(btnRow);
	}

	private ColorRect CreateOverlay()
	{
		var overlay = new ColorRect { Name = OverlayName, Color = new Color(0, 0, 0, 0.75f), MouseFilter = MouseFilterEnum.Stop, ZIndex = 1000 };
		overlay.SetAnchorsPreset(LayoutPreset.FullRect);
		_hooks.Owner.AddChild(overlay);
		return overlay;
	}

	private static VBoxContainer CreateContainer(ColorRect overlay)
	{
		var center = UIStyle.CreateCenteredOverlay();
		overlay.AddChild(center);

		var panel = new PanelContainer { CustomMinimumSize = new Vector2(480, 260) };
		var style = new StyleBoxFlat
		{
			BgColor = new Color(0.12f, 0.12f, 0.18f, 0.98f),
			BorderColor = UIStyle.ColorCyanGlow,
			BorderWidthTop = 2,
			BorderWidthBottom = 2,
			BorderWidthLeft = 2,
			BorderWidthRight = 2,
			CornerRadiusTopLeft = 6,
			CornerRadiusTopRight = 6,
			CornerRadiusBottomLeft = 6,
			CornerRadiusBottomRight = 6
		};
		panel.AddThemeStyleboxOverride("panel", style);
		center.AddChild(panel);

		var margin = new MarginContainer();
		margin.Name = "Margin_MarginContainer";
		margin.AddThemeConstantOverride("margin_top", 18);
		margin.AddThemeConstantOverride("margin_bottom", 18);
		margin.AddThemeConstantOverride("margin_left", 20);
		margin.AddThemeConstantOverride("margin_right", 20);
		panel.AddChild(margin);

		var vbox = new VBoxContainer();
		vbox.Name = "Vbox_VBoxContainer";
		vbox.AddThemeConstantOverride("separation", 12);
		margin.AddChild(vbox);

		var title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		UIStyle.ApplyTitle(title, TranslationServer.Translate("REGISTER CREATOR PROFILE"), 18);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		vbox.AddChild(title);

		var desc = new Label
		{
			Text = TranslationServer.Translate("A new map key has been generated for your machine. It is registered under the Steam name this account is signed in with."),
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(440, 0),
			SizeFlagsHorizontal = SizeFlags.ExpandFill
		};
		desc.AddThemeFontSizeOverride("font_size", 12);
		desc.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
		vbox.AddChild(desc);

		return vbox;
	}

	private async void OnRegisterPressed(LineEdit lineEdit, Label errLabel, Button btnRegister, ColorRect overlay, string pubKeyStr, Key key)
	{
		string username = lineEdit.Text.Trim();

		btnRegister.Disabled = true;
		errLabel.Text = TranslationServer.Translate("Registering...");

		try
		{
			string? errText = await GameContext.EditorService.ProcessCreatorRegistrationAsync(username, pubKeyStr, key);

			if (string.IsNullOrEmpty(errText))
			{
				overlay.QueueFree();
				_hooks.ShowFeedback(TranslationServer.Translate("Creator registered successfully!"));
			}
			else
			{
				HandleRegistrationFailure(errText, errLabel);
				btnRegister.Disabled = false;
			}
		}
		catch (Exception ex)
		{
			errLabel.Text = TranslationServer.Translate("Error: ") + ex.Message;
			btnRegister.Disabled = false;
		}
	}

	private static void HandleRegistrationFailure(string errText, Label errLabel)
	{
		try
		{
			var errDoc = JsonDocument.Parse(errText);
			if (errDoc.RootElement.TryGetProperty("Message", out var msgProp))
			{
				errLabel.Text = msgProp.GetString();
			}
			else
			{
				errLabel.Text = TranslationServer.Translate("Registration failed.");
			}
		}
		catch
		{
			errLabel.Text = TranslationServer.Translate("Registration failed: ") + errText;
		}
	}
}
