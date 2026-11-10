// Keyboard and pointer handling the HUD owns directly: the unit-selection buttons, the focus/space/
// enter/tilde key cases, and the chat submit and receive paths.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Realm.Ecs;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.ReplaySystem;
using Realm.Client.Services;
using Realm.Client.UI.InGame;
using Realm.Client.Utils;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Vector3 = Godot.Vector3;
using static Godot.Control;
using static Godot.CanvasItem;


namespace Realm.Client.UI;

public partial class InGameHUD
{
	private void OnUnitSelectionButtonClicked(int index)
	{
		if (!SelectionClickPolicy.Apply(index, CenterCameraOnUnit)) return;
		RefreshUI(GameContext.Host!.SelectedUnits);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (HandleFocusNext(@event)) return;

		if (@event is InputEventKey keyEvent && keyEvent.Pressed)
		{
			if (keyEvent.Keycode == Key.Escape && HotkeyReference.Visible)
			{
				HotkeyReference.Close();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (HandleSpaceKey(keyEvent)) return;
			if (HandleEnterKey(keyEvent)) return;
			if (HandleTildeKey(keyEvent)) return;
		}
	}

	/// <summary>
	/// The idle button's pulse: the panel owns the button, so it owns the blink, and the HUD only supplies the count and
	/// the once-per-wave notice.
	/// </summary>
	private void UpdateIdlePulse(double delta) => MinimapControls.UpdateIdlePulse(delta, _viewModel.IdleCount, OnUnitsBecameIdle);

	private void OnUnitsBecameIdle(int idleCount) =>
		ShowFeedbackText(string.Format(TranslationServer.Translate("{0} unit(s) are idle! [F1] to select"), idleCount),
			new Color(0.9f, 0.7f, 0.2f));

	private bool HandleFocusNext(InputEvent @event)
	{
		if (!@event.IsActionPressed("ui_focus_next")) return false;
		if (GameContext.Host == null || GameContext.Host.SelectedUnits.Count <= 1) return false;

		bool reverse = Input.IsKeyPressed(Key.Shift);
		GameContext.Host.CycleSelectionFocus(reverse);
		RefreshUI(GameContext.Host.SelectedUnits);
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleSpaceKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Key.Space || IsChatActive) return false;

		CenterCameraOnSelectedUnit();
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleEnterKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Key.Enter || IsChatActive) return false;
		if (!IsHudElementVisible(HudElement.Chat, true)) return false;

		_chatPanelController?.ShowChatInput(keyEvent.ShiftPressed);
		GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleTildeKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Key.Quoteleft || keyEvent.Echo) return false;
		if (IsChatActive || !WasmConsoleWindow.IsSinglePlayerOrTestMode()) return false;

		GameContext.WasmConsole.ToggleVisibility();
		GetViewport().SetInputAsHandled();
		return true;
	}

	private void OnChatInputSubmitted(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return;
		if (TryTriggerCheat(text))
		{
			return;
		}
	}

	private void OnLobbyChatReceived(string senderName, string message, bool alliesOnly, long timestampUtcTicks)
	{
		_chatPanelController?.OnLobbyChatReceived(senderName, message, alliesOnly, timestampUtcTicks);
	}

	private HotkeyReferencePanel? _hotkeyReference;

}
