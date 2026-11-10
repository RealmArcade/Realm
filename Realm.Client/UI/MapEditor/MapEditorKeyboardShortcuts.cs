using System;
using Godot;
using Realm.Client.Services;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the shortcut handler needs from the editor: the node its viewport hangs off (for the focus owner and to mark
/// input handled) and the dismissal chain Escape drives.
/// </summary>
public sealed record KeyboardShortcutHooks(
	Node Owner,
	Func<bool> CloseTopmostOverlay);

/// <summary>
/// The editor's modal-key guard: Escape closes one overlay, Tab is swallowed so focus cannot wander behind a dialog, the
/// backquote toggles the wasm console, and the arrow keys are kept from scrolling the sidebar while the pointer drives the
/// 3D view. Text inputs keep their own keys, which is why every branch checks the focus owner first.
/// </summary>
public sealed class MapEditorKeyboardShortcuts
{
	private readonly KeyboardShortcutHooks _hooks;

	public MapEditorKeyboardShortcuts(KeyboardShortcutHooks hooks) => _hooks = hooks;

	public void Handle(InputEventKey keyEvent)
	{
		if (HandleEscapeKey(keyEvent)) return;
		if (HandleTabKey(keyEvent)) return;
		if (HandleConsoleKey(keyEvent)) return;
		HandleArrowKeys(keyEvent);
	}

	/// <summary>
	/// With the settings menu open Escape belongs to it, so the editor reports the key as consumed without marking it
	/// handled and lets the menu act on it.
	/// </summary>
	private bool HandleEscapeKey(InputEventKey keyEvent)
	{
		if (keyEvent.Echo || keyEvent.Keycode != Godot.Key.Escape) return false;
		if (GameContext.IsSettingsMenuOpen) return true;

		if (!_hooks.CloseTopmostOverlay()) return false;

		_hooks.Owner.GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleTabKey(InputEventKey keyEvent)
	{
		if (keyEvent.Keycode != Godot.Key.Tab) return false;

		if (FloatingDialogBase.HasAnyDialogOpen)
		{
			return true;
		}

		_hooks.Owner.GetViewport().SetInputAsHandled();
		return true;
	}

	private bool HandleConsoleKey(InputEventKey keyEvent)
	{
		if (keyEvent.Echo || keyEvent.Keycode != Godot.Key.Quoteleft) return false;
		if (IsFocusOwnerTextInput()) return true;
		if (!WasmConsoleWindow.IsSinglePlayerOrTestMode()) return false;

		GameContext.WasmConsole.ToggleVisibility();
		_hooks.Owner.GetViewport().SetInputAsHandled();
		return true;
	}

	private void HandleArrowKeys(InputEventKey keyEvent)
	{
		bool isArrow = keyEvent.Keycode == Godot.Key.Up || keyEvent.Keycode == Godot.Key.Down ||
		               keyEvent.Keycode == Godot.Key.Left || keyEvent.Keycode == Godot.Key.Right;
		if (!isArrow || IsFocusOwnerTextInput()) return;

		_hooks.Owner.GetViewport().SetInputAsHandled();
	}

	private bool IsFocusOwnerTextInput()
	{
		var focusOwner = _hooks.Owner.GetViewport().GuiGetFocusOwner();
		return focusOwner != null && (focusOwner is LineEdit || focusOwner is TextEdit);
	}
}
