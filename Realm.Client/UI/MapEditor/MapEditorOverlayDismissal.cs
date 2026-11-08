using System;
using Godot;
using Realm.Client.Services;
using Realm.Client.UI;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What the dismissal chain needs from the editor: the node to look overlays up in, the scale dialog (which the editor
/// owns through its controller rather than as a plain node), the way to close it, and the manual's close, which reports
/// whether it was open.
/// </summary>
public sealed record OverlayDismissalHooks(
	Node Owner,
	Control? ScaleMapDialog,
	Action CloseScaleMapDialog,
	Func<bool> CloseHelpPanel);

/// <summary>
/// One Escape press closes exactly one thing, in the same order the overlays stack: the confirmation first because it is
/// always on top of whatever opened it, then the transient progress overlays, then the agreement, the manual, and finally
/// the floating dialogs, which close their own topmost.
/// </summary>
internal sealed class MapEditorOverlayDismissal
{
	private readonly OverlayDismissalHooks _hooks;

	public MapEditorOverlayDismissal(OverlayDismissalHooks hooks) => _hooks = hooks;

	public bool CloseTopmost()
	{
		if (TryCloseConfirmationOverlay()) return true;
		if (TryCloseNodeOverlay("GenerationOverlay")) return true;
		if (TryCloseScaleMapDialog()) return true;
		if (TryCloseNodeOverlay("PublishInstructionsOverlay")) return true;
		if (TryCloseNodeOverlay("CreatorRegistrationOverlay")) return true;
		if (TryCloseAgreementOverlay()) return true;
		if (_hooks.CloseHelpPanel()) return true;

		if (FloatingDialogBase.HasAnyDialogOpen)
		{
			return FloatingDialogBase.CloseTopmostDialog();
		}

		return false;
	}

	/// <summary>
	/// The confirmation may carry a CancelAction that has to run instead of a plain free, because the deferred
	/// continuation it would have taken is what dismisses it.
	/// </summary>
	private bool TryCloseConfirmationOverlay()
	{
		var confirmOverlay = _hooks.Owner.GetNodeOrNull<Control>("ConfirmationOverlay")
			?? GameContext.UserInterface?.GetNodeOrNull<Control>("ConfirmationOverlay");

		if (confirmOverlay is { } overlay && IsValidOverlay(overlay))
		{
			if (overlay.HasMeta("CancelAction"))
			{
				var action = overlay.GetMeta("CancelAction").AsCallable();
				action.Call();
			}
			else
			{
				confirmOverlay.QueueFree();
			}

			return true;
		}

		return false;
	}

	private bool TryCloseNodeOverlay(string nodeName)
	{
		var overlay = _hooks.Owner.GetNodeOrNull<Control>(nodeName);
		if (!IsValidOverlay(overlay)) return false;

		overlay!.QueueFree();
		return true;
	}

	private bool TryCloseScaleMapDialog()
	{
		if (!IsValidOverlay(_hooks.ScaleMapDialog)) return false;

		_hooks.CloseScaleMapDialog();
		return true;
	}

	/// <summary>
	/// The agreement is the creator sign-up gate, so dismissing it is the same as declining it: back to the main menu.
	/// </summary>
	private bool TryCloseAgreementOverlay()
	{
		var agreementOverlay = _hooks.Owner.GetNodeOrNull<Control>("AgreementOverlay");
		if (!IsValidOverlay(agreementOverlay)) return false;

		agreementOverlay!.QueueFree();
		GameContext.UserInterface?.TransitionTo(GameScreen.MainMenu);
		return true;
	}

	/// <summary>
	/// A node counts as open only while it is in the tree and not already on its way out; a queued node would otherwise
	/// be dismissed twice by two Escape presses in the same frame.
	/// </summary>
	private static bool IsValidOverlay(Node? overlay)
	{
		return overlay != null && GodotObject.IsInstanceValid(overlay) && overlay.IsInsideTree() && !overlay.IsQueuedForDeletion();
	}
}
