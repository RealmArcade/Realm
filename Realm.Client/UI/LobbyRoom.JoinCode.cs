using Godot;
using Realm.Client.Network;
using Realm.Shared.Network;

namespace Realm.Client.UI;

/// <summary>
///     The join code field a lobby that gates its seats with a code shows the members who arrived without it. It is not a
///     password in front of the room: the player is already standing in the lobby, reading its seats and its chat, and this
///     field is what turns their presence into a seat.
/// </summary>
public partial class LobbyRoom
{
	private PanelContainer _joinCodePanel = null!;

	private LineEdit _joinCodeInput = null!;

	private Label _joinCodeMessage = null!;

	private Label _title = null!;

	/// <summary>Builds the join code field, hidden until the lobby says its seats are gated.</summary>
	private void CreateJoinCodeUI()
	{
		_joinCodePanel = new PanelContainer();
		_joinCodePanel.Name = "JoinCodePanel";
		_joinCodePanel.Visible = false;
		_joinCodePanel.AddThemeStyleboxOverride("panel", UIStyle.CreateBackdropPanel());
		AddChild(_joinCodePanel);
		_joinCodePanel.SetAnchorsPreset(LayoutPreset.CenterTop);
		_joinCodePanel.GrowHorizontal = GrowDirection.Both;
		_joinCodePanel.OffsetLeft = -260;
		_joinCodePanel.OffsetRight = 260;
		_joinCodePanel.OffsetTop = 140;
		_joinCodePanel.OffsetBottom = 260;

		var column = new VBoxContainer();
		column.Name = "JoinCodeColumn";
		column.AddThemeConstantOverride("separation", 8);
		_joinCodePanel.AddChild(column);

		var title = new Label();
		title.Name = "JoinCodeTitle_Label";
		_title = title;
		title.Text = TranslationServer.Translate("This lobby asks for a join code before it seats you.");
		title.HorizontalAlignment = HorizontalAlignment.Center;
		title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		title.CustomMinimumSize = new Vector2(500, 0);
		title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		title.AddThemeFontSizeOverride("font_size", 14);
		column.AddChild(title);

		var row = new HBoxContainer();
		row.Name = "JoinCodeRow";
		row.Alignment = BoxContainer.AlignmentMode.Center;
		row.AddThemeConstantOverride("separation", 10);
		column.AddChild(row);

		_joinCodeInput = new LineEdit();
		_joinCodeInput.Name = "JoinCodeInput_LineEdit";
		_joinCodeInput.CustomMinimumSize = new Vector2(240, 40);
		_joinCodeInput.MaxLength = LobbyLimits.MaximumJoinCodeLength;
		_joinCodeInput.PlaceholderText = TranslationServer.Translate("Join code");
		_joinCodeInput.AddThemeStyleboxOverride("normal", UIStyle.CreateTextInput(false));
		_joinCodeInput.AddThemeStyleboxOverride("focus", UIStyle.CreateTextInput(true));
		_joinCodeInput.TextSubmitted += _ => OnJoinCodeSubmitted();
		row.AddChild(_joinCodeInput);

		var submit = UIStyle.CreateActionButton("JoinCodeSubmit_Button", TranslationServer.Translate("ENTER"), 14);
		submit.Pressed += OnJoinCodeSubmitted;
		row.AddChild(submit);

		_joinCodeMessage = new Label();
		_joinCodeMessage.Name = "JoinCodeMessage_Label";
		_joinCodeMessage.HorizontalAlignment = HorizontalAlignment.Center;
		_joinCodeMessage.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_joinCodeMessage.CustomMinimumSize = new Vector2(500, 0);
		_joinCodeMessage.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_joinCodeMessage.AddThemeFontSizeOverride("font_size", 13);
		_joinCodeMessage.AddThemeColorOverride("font_color", new Color(0.9f, 0.3f, 0.3f));
		column.AddChild(_joinCodeMessage);

		Lobby.JoinCodeRequirementChanged += OnJoinCodeRequirementChanged;
		Lobby.JoinCodeRefused += OnJoinCodeRefused;
		Lobby.PlayerListUpdated += UpdateJoinCodeVisibility;

		UpdateJoinCodeVisibility();
	}

	/// <summary>
	///     Sends the code the player typed to the authority, which is the only place it is compared. The host's own field sets
	///     the code instead, because the host is the one who decides whether the lobby gates its seats at all.
	/// </summary>
	private void OnJoinCodeSubmitted()
	{
		string typed = (_joinCodeInput.Text ?? string.Empty).Trim();

		if (Lobby.IsHost)
		{
			Lobby.SetJoinCode(typed);
			_joinCodeInput.Text = string.Empty;
			_joinCodeMessage.Text = typed.Length > 0
				? TranslationServer.Translate("This lobby now asks for that code before it seats a player.")
				: TranslationServer.Translate("This lobby no longer asks for a join code.");
			return;
		}

		if (typed.Length == 0) return;

		Lobby.PresentJoinCode(typed);
	}

	/// <summary>
	///     Shows the field when the lobby gates its seats and this screen does not hold one yet, and takes it away the
	///     moment the seat is held, which is what a cleared code does without a reconnect.
	/// </summary>
	private void UpdateJoinCodeVisibility()
	{
		if (_joinCodePanel == null) return;

		if (Lobby.IsHost)
		{
			// The host writes the code rather than presenting it, so the field is always offered and says what it is for.
			_joinCodePanel.Visible = true;
			if (_title != null) _title.Text = TranslationServer.Translate("Set a join code so only players who know it can take a seat. Leave it empty to let anyone who can see the lobby join.");
			return;
		}

		_joinCodePanel.Visible = Lobby.JoinCodeRequired && !Lobby.JoinCodeSeatHeld;
	}

	private void OnJoinCodeRequirementChanged(bool required)
	{
		if (!required)
		{
			_joinCodeInput.Text = string.Empty;
			_joinCodeMessage.Text = string.Empty;
		}

		UpdateJoinCodeVisibility();
	}

	private void OnJoinCodeRefused(string message)
	{
		if (Lobby.IsHost) return;

		_joinCodeMessage.Text = string.IsNullOrEmpty(message)
			? string.Empty
			: TranslationServer.Translate("That join code does not match this lobby's.");

		UpdateJoinCodeVisibility();
	}
}
