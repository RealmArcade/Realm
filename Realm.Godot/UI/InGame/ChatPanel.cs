using Godot;
using System;

public class ChatPanel
{
	private PanelContainer _chatPanelNode;
	private LineEdit _chatInput;
	private RichTextLabel _chatLog;
	private bool _isChatActive = false;
	private Label _chatPrefixLabel;
	private HBoxContainer _inputRow;
	private ChatMode _currentMode = ChatMode.Allies;

	private enum ChatMode
	{
		AllPlayers,
		Allies
	}

	public bool IsChatActive => _isChatActive;

	public ChatPanel(PanelContainer chatPanelNode, LineEdit chatInput, RichTextLabel chatLog)
	{
		_chatPanelNode = chatPanelNode;
		_chatInput = chatInput;
		_chatLog = chatLog;

		var chatContainer = _chatInput.GetParent();
		if (chatContainer != null)
		{
			chatContainer.RemoveChild(_chatInput);

			_inputRow = new HBoxContainer();
			_inputRow.Name = "ChatInputRow";
			_inputRow.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

			_chatPrefixLabel = new Label();
			_chatPrefixLabel.Name = "ChatPrefixLabel";
			_chatPrefixLabel.Text = TranslationServer.Translate("Allies: ");
			_chatPrefixLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.7f, 1.0f));
			_inputRow.AddChild(_chatPrefixLabel);

			_inputRow.AddChild(_chatInput);
			chatContainer.AddChild(_inputRow);

			_chatInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		}

		_chatInput.TextSubmitted += OnChatInputSubmitted;
		if (_inputRow != null)
		{
			_inputRow.Visible = false;
		}
		_chatInput.Visible = false;

		_chatInput.GuiInput += (ev) =>
		{
			if (ev is InputEventKey keyEv && keyEv.Pressed)
			{
				if (keyEv.Keycode == Key.Tab)
				{
					CycleChatMode();
					_chatInput.AcceptEvent();
				}
				else if (keyEv.Keycode == Key.Escape)
				{
					HideChatInput();
					_chatInput.AcceptEvent();
				}
			}
		};

		UpdateChatPanelActiveState(false);
	}

	private void UpdateChatPanelActiveState(bool active)
	{
		_isChatActive = active;
		if (_chatPanelNode == null) return;

		var styleBox = _chatPanelNode.GetThemeStylebox("panel") as StyleBoxFlat;
		if (styleBox != null)
		{
			if (active)
			{
				styleBox.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.85f);
				styleBox.BorderColor = new Color(0.25f, 0.25f, 0.25f, 0.85f);
			}
			else
			{
				styleBox.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.25f);
				styleBox.BorderColor = new Color(0.25f, 0.25f, 0.25f, 0.15f);
			}
			_chatPanelNode.AddThemeStyleboxOverride("panel", styleBox);
		}

		var filter = active ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
		_chatPanelNode.MouseFilter = filter;
		if (_chatLog != null)
		{
			_chatLog.MouseFilter = filter;
		}

		var chatContainer = _chatPanelNode.GetNodeOrNull<Control>("ChatContainer");
		if (chatContainer != null)
		{
			chatContainer.MouseFilter = filter;
		}
	}

	public void ShowChatInput(bool allPlayersMode)
	{
		_currentMode = allPlayersMode ? ChatMode.AllPlayers : ChatMode.Allies;
		UpdatePrefixLabel();
		UpdateChatPanelActiveState(true);
		if (_inputRow != null)
		{
			_inputRow.Visible = true;
		}
		_chatInput.Visible = true;
		_chatInput.GrabFocus();
	}

	public void HideChatInput()
	{
		UpdateChatPanelActiveState(false);
		if (_inputRow != null)
		{
			_inputRow.Visible = false;
		}
		_chatInput.Visible = false;
		_chatInput.ReleaseFocus();
		_chatInput.Text = "";
	}

	private void CycleChatMode()
	{
		_currentMode = _currentMode == ChatMode.AllPlayers ? ChatMode.Allies : ChatMode.AllPlayers;
		UpdatePrefixLabel();
	}

	private void UpdatePrefixLabel()
	{
		if (_chatPrefixLabel != null)
		{
			if (_currentMode == ChatMode.AllPlayers)
			{
				_chatPrefixLabel.Text = TranslationServer.Translate("All Players: ");
				_chatPrefixLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
			}
			else
			{
				_chatPrefixLabel.Text = TranslationServer.Translate("Allies: ");
				_chatPrefixLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.7f, 1.0f));
			}
		}
	}

	private void OnChatInputSubmitted(string text)
	{
		HideChatInput();
		if (string.IsNullOrWhiteSpace(text)) return;

		bool isMultiplayer = IsMultiplayer();
		if (TryHandleCheat(text, isMultiplayer)) return;

		string trimmedText = text.Trim();
		if (TryHandleCommand(trimmedText)) return;

		DispatchChatMessage(trimmedText);
	}

	private bool IsMultiplayer()
	{
		if (LobbyManager.Instance == null || LobbyManager.Instance.IsSinglePlayer) return false;
		if (InGameHUD.Instance != null && (InGameHUD.Instance.Multiplayer.MultiplayerPeer == null || InGameHUD.Instance.Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer))
		{
			return false;
		}
		return true;
	}

	private bool TryHandleCheat(string text, bool isMultiplayer)
	{
		if (isMultiplayer || InGameHUD.Instance == null) return false;
		return InGameHUD.Instance.TryTriggerCheat(text);
	}

	private bool TryHandleCommand(string trimmedText)
	{
		if (!trimmedText.Equals("/pause", StringComparison.OrdinalIgnoreCase)) return false;
		if (GameHost.Instance != null)
		{
			GameHost.Instance.TogglePauseRequest();
		}
		return true;
	}

	private void DispatchChatMessage(string trimmedText)
	{
		string sender = LobbyManager.Instance?.LocalPlayer?.Name ?? "Player";
		bool alliesOnly = _currentMode == ChatMode.Allies;

		if (LobbyManager.Instance != null)
		{
			LobbyManager.Instance.SendChatMessage(sender, trimmedText, alliesOnly);
		}
		else
		{
			OnLobbyChatReceived(sender, trimmedText, alliesOnly);
		}
	}

	public void OnLobbyChatReceived(string senderName, string message, bool alliesOnly = false)
	{
		if (_chatLog == null) return;
		string cleanMsg = message.Replace("[", "[[").Replace("]", "]]");
		string prefix = alliesOnly ? "[color=#00a2ff](Allies)[/color] " : "";
		string textToAppend = $"[color=#a0a0a0][{DateTime.Now:HH:mm:ss}][/color] {prefix}[color=#00ffc8]{senderName}:[/color] {cleanMsg}\n";
		_chatLog.AppendText(textToAppend);
	}
}
