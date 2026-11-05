using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using HttpClient = System.Net.Http.HttpClient;

namespace Realm.Client.UI;

/// <summary>
///     The announcements sign-up. It posts the address straight to the Sender form the website embeds, so signing up from
///     the game needs no browser and no API token: a form id is public by design, and the service's answer is the only
///     statement about whether the address was accepted.
/// </summary>
[GlobalClass]
public partial class AnnouncementsDialog : Control
{
	private const string SubscriptionEndpoint = "https://stats.sender.net/forms/aQWwnG/subscribe";
	private const string WebsiteUrl = "https://www.realm-game.com/";
	private const int CardWidth = 560;
	private const int CardInsetX = 30;
	private const int CardInsetY = 26;
	private const int CardInsetBottom = 22;
	private const int StonePanelPaddingX = 20;
	private const int TextFieldHeight = 38;
	private const int CloseButtonWidth = 132;
	private const int ContentWidth = CardWidth - 2 * (CardInsetX + StonePanelPaddingX);

	private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };
	private static readonly Color FailureColor = new(0.95f, 0.6f, 0.4f);
	private static readonly Color BodyTextColor = new(0.88f, 0.85f, 0.78f);

	private VBoxContainer _formBox;
	private VBoxContainer _confirmationBox;
	private LineEdit _nameEdit;
	private LineEdit _emailEdit;
	private Button _subscribeButton;
	private Label _statusLabel;
	private bool _busy;

	[GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
	private static partial Regex EmailAddressPattern();

	public override void _Ready()
	{
		Name = "AnnouncementsDialog";
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
	}

	public void Open()
	{
		_nameEdit.Text = ResolveDefaultName();
		_emailEdit.Text = string.Empty;
		_formBox.Visible = true;
		_confirmationBox.Visible = false;
		ClearStatus();
		Visible = true;
		_emailEdit.GrabFocus();
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "AnnouncementsDialogBackdrop", Color = new Color(0.02f, 0.02f, 0.04f, 0.82f) };
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		backdrop.GuiInput += OnBackdropInput;
		AddChild(backdrop);

		var center = new CenterContainer { Name = "AnnouncementsDialogCenter" };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		center.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(center);

		var card = new PanelContainer { Name = "AnnouncementsDialogCard" };
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "AnnouncementsDialogMargin" };
		margin.AddThemeConstantOverride("margin_left", CardInsetX);
		margin.AddThemeConstantOverride("margin_right", CardInsetX);
		margin.AddThemeConstantOverride("margin_top", CardInsetY);
		margin.AddThemeConstantOverride("margin_bottom", CardInsetBottom);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "AnnouncementsDialogColumn" };
		column.AddThemeConstantOverride("separation", 12);
		margin.AddChild(column);

		var titleLabel = new Label { Name = "AnnouncementsDialogTitle" };
		UIStyle.ApplyTitle(titleLabel, "FOLLOW REALM'S DEVELOPMENT", 28);
		titleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		titleLabel.CustomMinimumSize = new Vector2(ContentWidth, 0);
		titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(titleLabel);

		Label subtitleLabel = CreateBodyLabel(column, TranslationServer.Translate("No spam. Only important announcements of major milestones."));
		subtitleLabel.Name = "AnnouncementsDialogSubtitle";
		subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;

		var divider = new HSeparator { Name = "AnnouncementsDialogDivider" };
		divider.AddThemeStyleboxOverride("separator", UIStyle.CreateHairlineSeparator());
		column.AddChild(divider);

		_formBox = new VBoxContainer { Name = "AnnouncementsDialogForm" };
		_formBox.AddThemeConstantOverride("separation", 14);
		column.AddChild(_formBox);

		_nameEdit = CreateField(_formBox, "NameField", TranslationServer.Translate("Your name"), string.Empty);
		_emailEdit = CreateField(_formBox, "EmailField", TranslationServer.Translate("Email"), "your.email@example.com");
		_nameEdit.TextSubmitted += _ =>
		{
			StartSubscribe();
		};
		_emailEdit.TextSubmitted += _ =>
		{
			StartSubscribe();
		};

		_subscribeButton = new Button { Name = "AnnouncementsDialogSubscribeButton" };
		_subscribeButton.CustomMinimumSize = new Vector2(0, 46);
		_subscribeButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxes(_subscribeButton);
		UIStyle.ApplyButtonText(_subscribeButton, "SUBSCRIBE", 17);
		_subscribeButton.Pressed += StartSubscribe;
		_subscribeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		_formBox.AddChild(_subscribeButton);

		_confirmationBox = new VBoxContainer { Name = "AnnouncementsDialogConfirmation" };
		_confirmationBox.AddThemeConstantOverride("separation", 8);
		_confirmationBox.Visible = false;
		column.AddChild(_confirmationBox);

		Label confirmationLabel = CreateBodyLabel(_confirmationBox, TranslationServer.Translate("Subscribed. We'll keep you updated on important announcements."));
		confirmationLabel.Name = "AnnouncementsDialogConfirmationLabel";
		confirmationLabel.HorizontalAlignment = HorizontalAlignment.Center;
		confirmationLabel.AddThemeFontSizeOverride("font_size", 15);
		confirmationLabel.AddThemeColorOverride("font_color", UIStyle.ColorGold);

		_statusLabel = CreateBodyLabel(column, string.Empty);
		_statusLabel.Name = "AnnouncementsDialogStatus";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;

		var footer = new HBoxContainer { Name = "AnnouncementsDialogFooter" };
		footer.Alignment = BoxContainer.AlignmentMode.End;
		column.AddChild(footer);

		var closeButton = new Button { Name = "AnnouncementsDialogCloseButton" };
		closeButton.CustomMinimumSize = new Vector2(CloseButtonWidth, 34);
		closeButton.AddThemeConstantOverride("icon_max_width", 0);
		closeButton.FocusMode = FocusModeEnum.None;
		UIStyle.ApplyButtonStyleBoxes(closeButton);
		UIStyle.ApplyButtonText(closeButton, "CLOSE", 14);
		closeButton.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		closeButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		closeButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		closeButton.Pressed += Close;
		closeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(closeButton);
	}

	private LineEdit CreateField(VBoxContainer parent, string fieldName, string fieldLabel, string placeholderText)
	{
		var field = new VBoxContainer { Name = $"AnnouncementsDialog{fieldName}Group" };
		field.AddThemeConstantOverride("separation", 4);
		parent.AddChild(field);

		var label = new Label { Name = $"AnnouncementsDialog{fieldName}Label", Text = fieldLabel };
		label.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		label.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.07f, 0.8f));
		label.AddThemeConstantOverride("outline_size", 3);
		label.AddThemeFontSizeOverride("font_size", 13);
		field.AddChild(label);

		var edit = new LineEdit { Name = $"AnnouncementsDialog{fieldName}Edit", PlaceholderText = placeholderText };
		edit.CustomMinimumSize = new Vector2(0, TextFieldHeight);
		edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		edit.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		UIStyle.ApplyTextInput(edit);
		field.AddChild(edit);
		return edit;
	}

	private Label CreateBodyLabel(Container parent, string text)
	{
		var label = new Label { Name = "AnnouncementsDialogBodyLabel" };
		label.Text = text;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(ContentWidth, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.AddThemeColorOverride("font_color", BodyTextColor);
		label.AddThemeFontSizeOverride("font_size", 14);
		parent.AddChild(label);
		return label;
	}

	private static string ResolveDefaultName()
	{
		if (GameContext.Account.IsSignedIn) return GameContext.Account.DisplayName;

		return GameContext.Steam.UserName;
	}

	private void StartSubscribe()
	{
		_ = SubscribeAsync();
	}

	private async Task SubscribeAsync()
	{
		if (_busy) return;

		string emailAddress = _emailEdit.Text.Trim();
		if (!EmailAddressPattern().IsMatch(emailAddress))
		{
			ShowStatus(TranslationServer.Translate("Enter a valid email address."), FailureColor);
			_emailEdit.GrabFocus();
			return;
		}

		SetBusy(true);
		ShowStatus(TranslationServer.Translate("Subscribing..."), UIStyle.ColorGoldDull);

		FormSubmissionResult result = await SubmitAsync(emailAddress, _nameEdit.Text.Trim());

		if (!GodotObject.IsInstanceValid(this)) return;
		SetBusy(false);

		if (!result.Accepted)
		{
			ShowStatus(result.Message, FailureColor);
			_emailEdit.GrabFocus();
			return;
		}

		_formBox.Visible = false;
		_confirmationBox.Visible = true;
		ClearStatus();
	}

	private static async Task<FormSubmissionResult> SubmitAsync(string emailAddress, string name)
	{
		var payload = new JsonObject
		{
			["email"] = emailAddress,
			["firstname"] = name,
			["site_visit"] = true,
			["url"] = WebsiteUrl
		};

		try
		{
			using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await SharedHttpClient.PostAsync(SubscriptionEndpoint, content);
			string body = await response.Content.ReadAsStringAsync();
			return ReadSubmissionResult(body);
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
		{
			return FormSubmissionResult.Refused(TranslationServer.Translate("The announcements service could not be reached. Check your connection and try again."));
		}
	}

	private static FormSubmissionResult ReadSubmissionResult(string body)
	{
		if (body.Length == 0)
		{
			return FormSubmissionResult.Refused(TranslationServer.Translate("The announcements service refused the request. Try again later."));
		}

		try
		{
			using JsonDocument document = JsonDocument.Parse(body);
			JsonElement root = document.RootElement;

			if (root.TryGetProperty("success", out JsonElement successElement) && successElement.ValueKind == JsonValueKind.True)
			{
				return FormSubmissionResult.Succeeded;
			}

			if (root.TryGetProperty("errors", out JsonElement errors)
				&& errors.ValueKind == JsonValueKind.Object
				&& errors.TryGetProperty("email", out JsonElement emailError)
				&& emailError.GetString() is { Length: > 0 } emailMessage)
			{
				return FormSubmissionResult.Refused(emailMessage);
			}

			if (root.TryGetProperty("message", out JsonElement messageElement) && messageElement.GetString() is { Length: > 0 } message)
			{
				return FormSubmissionResult.Refused(message);
			}
		}
		catch (JsonException)
		{
		}

		return FormSubmissionResult.Refused(TranslationServer.Translate("The announcements service refused the request. Try again later."));
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		_subscribeButton.Disabled = busy;
		_nameEdit.Editable = !busy;
		_emailEdit.Editable = !busy;
	}

	private void ShowStatus(string text, Color color)
	{
		_statusLabel.Text = text;
		_statusLabel.AddThemeColorOverride("font_color", color);
	}

	private void ClearStatus()
	{
		_statusLabel.Text = string.Empty;
	}

	private void OnBackdropInput(InputEvent inputEvent)
	{
		if (inputEvent is InputEventMouseButton mouseButton && mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.Left)
		{
			Close();
		}
	}

	private void Close()
	{
		GameContext.UserInterface?.PlayClickSound();
		QueueFree();
	}


	private readonly record struct FormSubmissionResult(bool Accepted, string Message)
	{
		public static FormSubmissionResult Succeeded => new(true, string.Empty);

		public static FormSubmissionResult Refused(string message) => new(false, message);
	}
}
