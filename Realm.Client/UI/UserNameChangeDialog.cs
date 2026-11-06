using System;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Services;
using Realm.Shared.Accounts;

namespace Realm.Client.UI;

/// <summary>
///     The one place a username is typed. The profile card states the name as a label rather than a field, so this card is the
///     only surface that ever asks for one: it shows the name being replaced, collects the name to replace it with, and hands
///     the pair to whatever the caller needs done with it. A rename, a local name while signed out and the name a refused
///     Steam persona has to be exchanged for are the same card with different words, so nothing about a first sign-in looks
///     different from a later rename.
/// </summary>
[GlobalClass]
public partial class UserNameChangeDialog : Control
{
	private const int CardWidth = 480;
	private const int CardInsetX = 28;
	private const int CardInsetY = 24;
	private const int CardInsetBottom = 20;
	private const int ContentWidth = CardWidth - 2 * CardInsetX;
	private const int TextFieldHeight = 38;
	private const int ActionButtonWidth = 150;

	private static readonly Color FailureColor = new(0.95f, 0.6f, 0.4f);
	private static readonly Color BodyTextColor = new(0.88f, 0.85f, 0.78f);

	private Label _titleLabel;
	private Label _hintLabel;
	private Label _currentNameValueLabel;
	private LineEdit _newNameEdit;
	private Button _changeButton;
	private Button _cancelButton;
	private Label _statusLabel;
	private Func<string, Task<UserNameChangeResult>> _submit;
	private bool _busy;

	public override void _Ready()
	{
		Name = nameof(UserNameChangeDialog);
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;

		BuildLayout();
		Visible = false;
	}

	/// <summary>
	///     Opens the card over everything already on screen. <paramref name="titleText" /> and <paramref name="hintText" /> are
	///     already translated by the caller, which is the only side that knows why the name is being changed.
	/// </summary>
	public void Open(string titleText, string currentName, string suggestedName, string hintText, Func<string, Task<UserNameChangeResult>> submitAsync)
	{
		_submit = submitAsync;

		_titleLabel.Text = titleText;
		_hintLabel.Text = hintText;
		_currentNameValueLabel.Text = currentName;

		_newNameEdit.Text = suggestedName;
		_newNameEdit.CaretColumn = suggestedName.Length;
		_newNameEdit.SelectAll();

		ClearStatus();
		SetBusy(false);
		Visible = true;
		_newNameEdit.GrabFocus();
	}

	private void BuildLayout()
	{
		var backdrop = new ColorRect { Name = "UserNameChangeBackdrop", Color = new Color(0.02f, 0.02f, 0.04f, 0.82f) };
		backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		backdrop.MouseFilter = MouseFilterEnum.Stop;
		backdrop.GuiInput += OnBackdropInput;
		AddChild(backdrop);

		var center = new CenterContainer { Name = "UserNameChangeCenter" };
		center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		center.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(center);

		var card = new PanelContainer { Name = "UserNameChangeCard" };
		card.CustomMinimumSize = new Vector2(CardWidth, 0);
		card.AddThemeStyleboxOverride("panel", UIStyle.CreateStonePanel(true));
		center.AddChild(card);

		var margin = new MarginContainer { Name = "UserNameChangeMargin" };
		margin.AddThemeConstantOverride("margin_left", CardInsetX);
		margin.AddThemeConstantOverride("margin_right", CardInsetX);
		margin.AddThemeConstantOverride("margin_top", CardInsetY);
		margin.AddThemeConstantOverride("margin_bottom", CardInsetBottom);
		card.AddChild(margin);

		var column = new VBoxContainer { Name = "UserNameChangeColumn" };
		column.AddThemeConstantOverride("separation", 12);
		margin.AddChild(column);

		_titleLabel = new Label { Name = "UserNameChangeTitle" };
		UIStyle.ApplyTitle(_titleLabel, string.Empty, 24);
		_titleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_titleLabel.CustomMinimumSize = new Vector2(ContentWidth, 0);
		_titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		column.AddChild(_titleLabel);

		_hintLabel = CreateBodyLabel(column, string.Empty);
		_hintLabel.Name = "UserNameChangeHint";
		_hintLabel.HorizontalAlignment = HorizontalAlignment.Center;

		var divider = new HSeparator { Name = "UserNameChangeDivider" };
		divider.AddThemeStyleboxOverride("separator", UIStyle.CreateHairlineSeparator());
		column.AddChild(divider);

		_currentNameValueLabel = CreateReadOnlyValueRow(column, "UserNameChangeOld", TranslationServer.Translate("Old Username"));
		_newNameEdit = CreateNewNameField(column);

		_statusLabel = CreateBodyLabel(column, string.Empty);
		_statusLabel.Name = "UserNameChangeStatus";
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Center;

		var footer = new HBoxContainer { Name = "UserNameChangeFooter" };
		footer.Alignment = BoxContainer.AlignmentMode.Center;
		footer.AddThemeConstantOverride("separation", 12);
		column.AddChild(footer);

		_changeButton = new Button { Name = "UserNameChangeConfirmButton" };
		_changeButton.CustomMinimumSize = new Vector2(ActionButtonWidth, 40);
		_changeButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_changeButton);
		UIStyle.ApplyButtonText(_changeButton, TranslationServer.Translate("CHANGE"), 16);
		_changeButton.Pressed += OnChangePressed;
		_changeButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(_changeButton);

		_cancelButton = new Button { Name = "UserNameChangeCancelButton" };
		_cancelButton.CustomMinimumSize = new Vector2(ActionButtonWidth, 40);
		_cancelButton.AddThemeConstantOverride("icon_max_width", 0);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_cancelButton);
		UIStyle.ApplyButtonText(_cancelButton, TranslationServer.Translate("CANCEL"), 16);
		_cancelButton.AddThemeColorOverride("font_color", UIStyle.ColorCyanGlow);
		_cancelButton.AddThemeColorOverride("font_hover_color", UIStyle.ColorGold);
		_cancelButton.AddThemeColorOverride("font_pressed_color", UIStyle.ColorCyanGlow);
		_cancelButton.Pressed += Cancel;
		_cancelButton.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		footer.AddChild(_cancelButton);
	}

	private Label CreateReadOnlyValueRow(VBoxContainer parent, string fieldName, string fieldLabel)
	{
		var field = new VBoxContainer { Name = $"{fieldName}Group" };
		field.AddThemeConstantOverride("separation", 4);
		parent.AddChild(field);

		CreateFieldLabel(field, $"{fieldName}Label", fieldLabel);

		var value = new Label { Name = $"{fieldName}Value" };
		value.Text = string.Empty;
		value.CustomMinimumSize = new Vector2(0, TextFieldHeight - 10);
		value.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		value.VerticalAlignment = VerticalAlignment.Center;
		value.AddThemeColorOverride("font_color", UIStyle.ColorGold);
		value.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.07f, 0.8f));
		value.AddThemeConstantOverride("outline_size", 3);
		value.AddThemeFontSizeOverride("font_size", 17);
		field.AddChild(value);

		return value;
	}

	private LineEdit CreateNewNameField(VBoxContainer parent)
	{
		var field = new VBoxContainer { Name = "UserNameChangeNewGroup" };
		field.AddThemeConstantOverride("separation", 4);
		parent.AddChild(field);

		CreateFieldLabel(field, "UserNameChangeNewLabel", TranslationServer.Translate("New Username"));

		var edit = new LineEdit { Name = "UserNameChangeNewEdit" };
		edit.CustomMinimumSize = new Vector2(0, TextFieldHeight);
		edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		edit.MaxLength = DisplayNameRules.MaximumUtf8Bytes;
		UIStyle.ApplyTextInput(edit);
		edit.TextSubmitted += _ => OnChangePressed();
		edit.MouseEntered += () => GameContext.UserInterface?.PlayHoverSound();
		field.AddChild(edit);
		return edit;
	}

	private static Label CreateFieldLabel(VBoxContainer parent, string fieldName, string fieldLabel)
	{
		var label = new Label { Name = fieldName, Text = fieldLabel };
		label.AddThemeColorOverride("font_color", BodyTextColor);
		label.AddThemeFontSizeOverride("font_size", 13);
		parent.AddChild(label);
		return label;
	}

	private Label CreateBodyLabel(Container parent, string text)
	{
		var label = new Label { Name = "UserNameChangeBodyLabel" };
		label.Text = text;
		label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		label.CustomMinimumSize = new Vector2(ContentWidth, 0);
		label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		label.AddThemeColorOverride("font_color", BodyTextColor);
		label.AddThemeFontSizeOverride("font_size", 14);
		parent.AddChild(label);
		return label;
	}

	private void OnChangePressed()
	{
		if (_busy || _submit == null) return;

		GameContext.UserInterface?.PlayClickSound();

		string newName = _newNameEdit.Text.Trim();
		if (newName.Length == 0)
		{
			ShowStatus(TranslationServer.Translate("Enter a new username."), FailureColor);
			_newNameEdit.GrabFocus();
			return;
		}

		_ = SubmitAsync(newName);
	}

	private async Task SubmitAsync(string newName)
	{
		SetBusy(true);
		ShowStatus(TranslationServer.Translate("Saving your username..."), UIStyle.ColorGoldDull);

		UserNameChangeResult result = await _submit(newName);

		if (!GodotObject.IsInstanceValid(this)) return;
		SetBusy(false);

		if (!result.Accepted)
		{
			ShowStatus(result.Message, FailureColor);
			_newNameEdit.GrabFocus();
			_newNameEdit.SelectAll();
			return;
		}

		QueueFree();
	}

	private void SetBusy(bool busy)
	{
		_busy = busy;
		_changeButton.Disabled = busy;
		_cancelButton.Disabled = busy;
		_newNameEdit.Editable = !busy;
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
			Cancel();
		}
	}

	private void Cancel()
	{
		if (_busy) return;

		GameContext.UserInterface?.PlayClickSound();
		QueueFree();
	}
}

/// <summary>
///     What one username change ended as. A refusal carries the sentence the player is shown beside the field, so the card that
///     asked for the name never has to know which rule the authority applied.
/// </summary>
public readonly record struct UserNameChangeResult(bool Accepted, string Message)
{
	public static UserNameChangeResult Succeeded => new(true, string.Empty);

	public static UserNameChangeResult Refused(string message) => new(false, message);
}
