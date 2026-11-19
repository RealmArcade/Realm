using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;
using Realm.Shared.Network;
using Realm.Shared.Steam;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The one control a hosted match offers the people playing it: did this map hold up?
///     <para>
///         It exists so a map's author has a reason to put it in front of a few people before strangers find it, and so an
///         accidentally published test map never reaches the public catalogue in the first place. The answer goes to the
///         authority for this match only; nothing is signed, uploaded or sent to Realm, and a player who ignores the chip
///         entirely is not asked twice.
///     </para>
///     <para>
///         It stays disabled until five minutes of the match have been played, because an answer from the first ten seconds
///         is about the loading screen. After voting it shows the code the tester hands to the map's author: an opaque
///         envelope that carries their SteamID and their answer, so one person counts once and so the code is safe to paste
///         where other people are reading.
///     </para>
/// </summary>
public sealed class PlaytestVoteChip
{
	private const int MinimumSecondsBeforeVote = 300;

	private readonly Control _root;
	private readonly Button _playtestedButton;
	private readonly Button _notReadyButton;
	private readonly Label _statusLabel;
	private readonly Button _copyButton;
	private readonly HBoxContainer _voteRow;

	private string _code = string.Empty;
	private bool _voted;
	private bool _lockedNoticeShown;

	private PlaytestVoteChip()
	{
		_root = new Control { Name = "PlaytestVoteChip" };
		_root.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		_root.OffsetLeft = -312;
		_root.OffsetTop = 96;
		_root.OffsetRight = -12;
		_root.OffsetBottom = 236;
		_root.MouseFilter = Control.MouseFilterEnum.Pass;

		var column = new VBoxContainer { Name = "PlaytestVoteColumn" };
		column.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		column.AddThemeConstantOverride("separation", 4);
		column.Alignment = BoxContainer.AlignmentMode.Begin;
		_root.AddChild(column);

		var title = new Label { Name = "PlaytestVoteTitle_Label" };
		title.Text = TranslationServer.Translate("PLAYTESTED?");
		title.HorizontalAlignment = HorizontalAlignment.Right;
		title.AddThemeFontSizeOverride("font_size", 12);
		title.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		column.AddChild(title);

		_voteRow = new HBoxContainer { Name = "PlaytestVoteRow" };
		_voteRow.Alignment = BoxContainer.AlignmentMode.End;
		_voteRow.AddThemeConstantOverride("separation", 6);
		column.AddChild(_voteRow);

		_playtestedButton = CreateVoteButton("PlaytestedButton", UnicodeIcons.THUMBS_UP, TranslationServer.Translate("Playtested - this map is ready for other players"));
		_playtestedButton.Pressed += () => Cast(PlaytestVote.Playtested);
		_voteRow.AddChild(_playtestedButton);

		_notReadyButton = CreateVoteButton("NotReadyButton", UnicodeIcons.THUMBS_DOWN, TranslationServer.Translate("Not ready - other players would find problems"));
		_notReadyButton.Pressed += () => Cast(PlaytestVote.NotReady);
		_voteRow.AddChild(_notReadyButton);

		_statusLabel = new Label { Name = "PlaytestVoteStatus_Label" };
		_statusLabel.Text = string.Empty;
		_statusLabel.HorizontalAlignment = HorizontalAlignment.Right;
		_statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_statusLabel.CustomMinimumSize = new Vector2(300, 0);
		_statusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_statusLabel.AddThemeFontSizeOverride("font_size", 11);
		_statusLabel.AddThemeColorOverride("font_color", UIStyle.ColorGoldDull);
		column.AddChild(_statusLabel);

		_copyButton = new Button { Name = "PlaytestCodeCopyButton" };
		_copyButton.Text = TranslationServer.Translate("COPY CODE");
		_copyButton.Set("icon_max_width", 0);
		_copyButton.AddThemeFontSizeOverride("font_size", 11);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(_copyButton);
		_copyButton.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
		_copyButton.Visible = false;
		_copyButton.Pressed += CopyCode;
		column.AddChild(_copyButton);

		ClientServerSession? session = GameContext.ServerSession;
		if (session != null)
		{
			session.PlaytestVoteAnswered += OnVoteAnswered;
			session.PlaytestVoteRecorded += OnVoteRecorded;
		}

		SetLockedState();
	}

	/// <summary>The node the HUD adds to its own tree.</summary>
	public Control Root => _root;

	/// <summary>
	///     Builds the chip for a match that is being hosted and played.
	/// </summary>
	/// <returns>The chip, ready to be added to the HUD.</returns>
	public static PlaytestVoteChip Create() => new();

	/// <summary>
	///     Keeps the chip's lock state and hint in step with the match, which is the only thing that changes while it is up.
	/// </summary>
	/// <param name="gameElapsedSeconds">How long the match has been running.</param>
	public void Update(float gameElapsedSeconds)
	{
		if (_voted) return;

		if (gameElapsedSeconds >= MinimumSecondsBeforeVote)
		{
			if (!_playtestedButton.Disabled) return;

			_playtestedButton.Disabled = false;
			_notReadyButton.Disabled = false;
			_statusLabel.Text = TranslationServer.Translate("Answers stay with you until you send one; the map's author counts them.");
			return;
		}

		SetLockedState();
	}

	/// <summary>
	///     Stops listening to the match, so a chip that leaves with its match cannot keep the session alive.
	/// </summary>
	public void Dispose()
	{
		ClientServerSession? session = GameContext.ServerSession;
		if (session == null) return;

		session.PlaytestVoteAnswered -= OnVoteAnswered;
		session.PlaytestVoteRecorded -= OnVoteRecorded;
	}

	private void SetLockedState()
	{
		_playtestedButton.Disabled = true;
		_notReadyButton.Disabled = true;

		if (_lockedNoticeShown) return;
		_lockedNoticeShown = true;
		_statusLabel.Text = string.Format(
			TranslationServer.Translate("Play {0} minutes of this match before answering for the map."),
			MinimumSecondsBeforeVote / 60);
	}

	private static Button CreateVoteButton(string name, string glyph, string tooltip)
	{
		var button = new Button
		{
			Name = name,
			Text = glyph,				TooltipText = tooltip,
			CustomMinimumSize = new Vector2(38, 30)
		};

		button.Set("icon_max_width", 0);
		button.FocusMode = Control.FocusModeEnum.None;
		button.AddThemeFontSizeOverride("font_size", 13);
		UIStyle.ApplyButtonStyleBoxesClearingFocus(button);
		return button;
	}

	private void Cast(PlaytestVote vote)
	{
		GameContext.UserInterface?.PlayClickSound();

		if (GameContext.ServerSession?.SendPlaytestVote(vote) != true)
		{
			_statusLabel.Text = TranslationServer.Translate("This match is no longer taking answers.");
			return;
		}

		_playtestedButton.Disabled = true;
		_notReadyButton.Disabled = true;
		_statusLabel.Text = TranslationServer.Translate("Sending your answer...");
	}

	private void OnVoteAnswered(PlaytestVoteAnswerMessage answer)
	{
		if (!answer.Accepted)
		{
			_statusLabel.Text = answer.Failure;
			_playtestedButton.Disabled = false;
			_notReadyButton.Disabled = false;
			return;
		}

		_voted = true;
		_code = answer.Code;
		// The answer stays changeable until the match ends, so both choices stay live; the authority replaces this seat's vote
		// and the ledger keeps one answer per Steam identity either way.
		_playtestedButton.Disabled = false;
		_notReadyButton.Disabled = false;
		_statusLabel.Text = TranslationServer.Translate("Thank you. This code tells the map's author who you are, so you count once. Paste it to them.");
		_copyButton.Visible = true;
	}

	private void OnVoteRecorded(PlaytestVoteRecordMessage record)
	{
		// Every screen in the match is told about every accepted vote, because the map's author may be any seat. Only the
		// screen hosting the match keeps the ledger, which is the machine the author publishes from.
		if (GameContext.Lobby is not { IsHost: true } && !GameContext.SessionState.IsSinglePlayerLobbyMode) return;

		// Filed under the map's name and this screen's Steam identity, which is the same key the editor publishes from, so
		// the count survives a fresh folder, a new version and a move onto a different Workshop item.
		string ledgerKey = PlaytestStore.BuildMapKey(record.MapTitle, GameContext.Steam.SteamId);
		GameContext.Playtest.TryAppendCode(ledgerKey, record.MapKey, record.Code, replaceExistingAnswer: true, out string failure);
		if (failure.Length > 0) GameLog.LogError($"[Playtest] {failure}");
	}

	private void CopyCode()
	{
		if (_code.Length == 0) return;

		DisplayServer.ClipboardSet(_code);
		GameContext.UserInterface?.PlayClickSound();
		_statusLabel.Text = TranslationServer.Translate("Code copied.");
	}
}
