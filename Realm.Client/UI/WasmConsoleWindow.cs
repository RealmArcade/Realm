using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Color = Godot.Color;
using FileAccess = Godot.FileAccess;

namespace Realm.Client.UI;

[GlobalClass]
public partial class WasmConsoleWindow : Window
{
	private static WasmConsoleWindow? _instance;

	public static WasmConsoleWindow Instance
	{
		get
		{
			if (_instance == null || !GodotObject.IsInstanceValid(_instance))
			{
				_instance = new WasmConsoleWindow();
				EnsureInTree();
			}

			return _instance;
		}
	}

	public static bool Exists => _instance != null && GodotObject.IsInstanceValid(_instance);

	public new bool IsVisible => Visible;

	private bool _hasErrors = false;

	public bool HasErrors
	{
		get => _hasErrors;
		set => _hasErrors = value;
	}

	private string _logFilePath = "";
	private readonly List<string> _bufferedLogs = new List<string>();
	private string _bufferedStatus = "Initializing compilation pipeline...";
	private string _bufferedStatusColor = "#00e5ff";
	private readonly object _logLock = new object();

	private ColorRect? _statusIndicator;
	private Label? _statusLabel;
	private RichTextLabel? _logLabel;
	private LineEdit? _commandInput;

	public static bool IsSinglePlayerOrTestMode()
	{
		if (GameContext.SessionState.IsTestMode) return true;
		if (GameHost.Instance != null && GameContext.SessionState.IsMapEditorMode) return true;
		if (GodotObject.IsInstanceValid(LobbyManager.Instance) && LobbyManager.Instance.IsSinglePlayer) return true;
		return false;
	}

	public WasmConsoleWindow()
	{
		Title = "⚙️ WASM COMPILATION & RUNTIME CONSOLE";
		Size = new Vector2I(760, 520);
		MinSize = new Vector2I(400, 300);
		Transient = false;
		Exclusive = false;
		Unresizable = false;
		WrapControls = false;
		Visible = false;
		InitialPosition = WindowInitialPosition.CenterMainWindowScreen;

		CloseRequested += OnCloseRequested;

		try
		{
			string userDir = ProjectSettings.GlobalizePath("user://");
			_logFilePath = Path.Combine(userDir, "wasm_compile.log");
		}
		catch
		{
			_logFilePath = "";
		}

		BuildUI();

		WasmRuntime.OnWasmLog += line => AppendLog(line);
	}

	private static void EnsureInTree()
	{
		if (_instance == null) return;
		var mainLoop = Engine.GetMainLoop();
		if (mainLoop is SceneTree tree && tree.Root != null)
		{
			if (_instance.GetParent() == null)
			{
				tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
			}
		}
	}

	private void BuildUI()
	{
		var mainPanel = new PanelContainer();
		mainPanel.Name = "MainPanel_PanelContainer";
		mainPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);

		var panelStyle = new StyleBoxFlat();
		panelStyle.BgColor = UIStyle.ColorBgDark;
		mainPanel.AddThemeStyleboxOverride("panel", panelStyle);

		AddChild(mainPanel);

		var marginContainer = new MarginContainer();
		marginContainer.Name = "MarginContainer_MarginContainer";
		marginContainer.AddThemeConstantOverride("margin_left", 12);
		marginContainer.AddThemeConstantOverride("margin_top", 12);
		marginContainer.AddThemeConstantOverride("margin_right", 12);
		marginContainer.AddThemeConstantOverride("margin_bottom", 12);
		mainPanel.AddChild(marginContainer);

		var mainVBox = new VBoxContainer();
		mainVBox.Name = "MainVBox_VBoxContainer";
		mainVBox.AddThemeConstantOverride("separation", 8);
		marginContainer.AddChild(mainVBox);

		var headerHBox = new HBoxContainer();
		headerHBox.Name = "HeaderHBox_HBoxContainer";
		headerHBox.AddThemeConstantOverride("separation", 8);
		mainVBox.AddChild(headerHBox);

		_statusIndicator = new ColorRect();
		_statusIndicator.Name = "StatusIndicator_ColorRect";
		_statusIndicator.CustomMinimumSize = new Vector2(12, 12);
		_statusIndicator.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		_statusIndicator.Color = new Color(_bufferedStatusColor);
		headerHBox.AddChild(_statusIndicator);

		_statusLabel = new Label();
		_statusLabel.Name = "StatusLabel_Label";
		_statusLabel.Text = _bufferedStatus;
		_statusLabel.Modulate = new Color(_bufferedStatusColor);
		_statusLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_statusLabel.VerticalAlignment = VerticalAlignment.Center;
		headerHBox.AddChild(_statusLabel);

		_logLabel = new RichTextLabel();
		_logLabel.Name = "LogLabel_RichTextLabel";
		_logLabel.SelectionEnabled = true;
		_logLabel.FocusMode = Control.FocusModeEnum.Click;
		_logLabel.BbcodeEnabled = true;
		_logLabel.ScrollFollowing = true;
		_logLabel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_logLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_logLabel.CustomMinimumSize = new Vector2(0, 200);

		var logAreaStyle = new StyleBoxFlat();
		logAreaStyle.BgColor = new Color(0.05f, 0.05f, 0.07f, 1.0f);
		logAreaStyle.SetCornerRadiusAll(4);
		logAreaStyle.ContentMarginLeft = 8;
		logAreaStyle.ContentMarginTop = 8;
		logAreaStyle.ContentMarginRight = 8;
		logAreaStyle.ContentMarginBottom = 8;
		_logLabel.AddThemeStyleboxOverride("normal", logAreaStyle);

		mainVBox.AddChild(_logLabel);

		var bottomHBox = new HBoxContainer();
		bottomHBox.Name = "BottomHBox_HBoxContainer";
		bottomHBox.AddThemeConstantOverride("separation", 8);
		mainVBox.AddChild(bottomHBox);

		_commandInput = new LineEdit();
		_commandInput.Name = "CommandInput_LineEdit";
		_commandInput.PlaceholderText = "Enter command or chat message...";
		_commandInput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_commandInput.TextSubmitted += OnCommandSubmitted;
		bottomHBox.AddChild(_commandInput);

		var copyButton = new Button();
		copyButton.Name = "CopyButton_Button";
		copyButton.Text = "Copy Logs";
		copyButton.Pressed += CopyLogsToClipboard;
		bottomHBox.AddChild(copyButton);

		var clearButton = new Button();
		clearButton.Name = "ClearButton_Button";
		clearButton.Text = "Clear";
		clearButton.Pressed += ClearLogs;
		bottomHBox.AddChild(clearButton);

		FlushLogsToUI();
	}

	private void OnCloseRequested()
	{
		Hide();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
		{
			if (keyEvent.Keycode == Key.Quoteleft || keyEvent.Keycode == Key.Escape)
			{
				GetViewport()?.SetInputAsHandled();
				Hide();
			}
		}
	}

	public void ToggleVisibility()
	{
		SetVisible(!Visible);
	}

	public void ShowConsole()
	{
		SetVisible(true);
	}

	public new void Show()
	{
		SetVisible(true);
	}

	public new void Hide()
	{
		SetVisible(false);
	}

	public new void SetVisible(bool visible)
	{
		Callable.From(() =>
		{
			EnsureInTree();
			Visible = visible;
			if (visible)
			{
				PopupCentered();
				GrabFocus();
				if (_commandInput != null)
				{
					_commandInput.GrabFocus();
				}
			}
		}).CallDeferred();
	}

	private void OnCommandSubmitted(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		string cmd = text.Trim();

		if (_commandInput != null)
		{
			_commandInput.Text = "";
		}

		AppendLog($"> {cmd}");

		Callable.From(() =>
		{
			try
			{
				if (GameHost.Instance != null)
				{
					GameHost.Instance.TriggerPlayerChatMessage(cmd);
				}
				else if (GodotObject.IsInstanceValid(LobbyManager.Instance))
				{
					LobbyManager.Instance.SendChatMessage(LobbyManager.Instance.AuthenticatedUsername ?? "Player", cmd);
				}
			}
			catch (Exception ex)
			{
				AppendLog($"[ERROR] Failed to dispatch command: {ex.Message}");
			}
		}).CallDeferred();
	}

	public void CopyLogsToClipboard()
	{
		Callable.From(() =>
		{
			string textToCopy = _logLabel != null ? _logLabel.GetParsedText() : string.Join("\n", _bufferedLogs);
			DisplayServer.ClipboardSet(textToCopy);
			SetStatus("✓ Console output copied to clipboard!", new Color(0, 0.9f, 1.0f));
		}).CallDeferred();
	}

	public void ClearLogs()
	{
		_hasErrors = false;
		lock (_logLock)
		{
			_bufferedLogs.Clear();
		}

		try
		{
			if (!string.IsNullOrEmpty(_logFilePath))
				File.WriteAllText(_logFilePath, $"=== WASM COMPILATION & RUNTIME LOG [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ===\n");
		}
		catch
		{
		}

		Callable.From(() =>
		{
			if (_logLabel != null)
			{
				_logLabel.Clear();
			}
		}).CallDeferred();
	}

	public void AppendLog(string line)
	{
		if (string.IsNullOrEmpty(line)) return;

		if (!string.IsNullOrEmpty(_logFilePath))
		{
			try
			{
				File.AppendAllText(_logFilePath, line + "\n");
			}
			catch
			{
			}
		}

		lock (_logLock)
		{
			_bufferedLogs.Add(line);
		}

		Callable.From(FlushLogsToUI).CallDeferred();
	}

	private void FlushLogsToUI()
	{
		lock (_logLock)
		{
			if (_logLabel == null || _bufferedLogs.Count == 0) return;

			foreach (var line in _bufferedLogs)
			{
				_logLabel.AppendText(line + "\n");
			}

			_bufferedLogs.Clear();
		}
	}

	public void SetStatus(string statusText, Color color)
	{
		SetStatus(statusText, $"#{color.ToHtml()}");
	}

	public void SetStatus(string statusText, string hexColor)
	{
		lock (_logLock)
		{
			_bufferedStatus = statusText;
			_bufferedStatusColor = hexColor;
		}

		Callable.From(UpdateStatusUI).CallDeferred();
	}

	private void UpdateStatusUI()
	{
		lock (_logLock)
		{
			if (_statusLabel != null)
			{
				_statusLabel.Text = _bufferedStatus;
				_statusLabel.Modulate = new Color(_bufferedStatusColor);
			}

			if (_statusIndicator != null)
			{
				_statusIndicator.Color = new Color(_bufferedStatusColor);
			}
		}
	}

	public void CleanUp()
	{
		Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(this))
			{
				QueueFree();
			}
			_instance = null;
		}).CallDeferred();
	}
}