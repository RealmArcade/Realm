using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using Realm.Shared.Distribution;
using static Godot.Control;

namespace Realm.Client.UI;

public partial class MapEditorHUD
{
	public bool IsMouseOverUI(Vector2 mousePos)
	{
		if (IsMenuOrOverlayOpen(mousePos)) return true;
		if (IsDraggingOrHoveringCoreUI()) return true;
		if (IsPopupVisible()) return true;
		if (IsMouseOverOptionCards(mousePos)) return true;
		if (IsMouseOverOptionContents(mousePos)) return true;
		if (IsMouseOverToolbarOrOverlays(mousePos)) return true;

		return false;
	}

	private bool IsMenuOrOverlayOpen(Vector2 mousePos)
	{
		if (GameContext.IsSettingsMenuOpen) return true;
		if (_helpOverlayPanel != null && _helpOverlayPanel.IsVisibleInTree()) return true;
		if (GetNodeOrNull<Control>("ConfirmationOverlay") != null) return true;
		if (GetNodeOrNull<Control>("GenerationOverlay") != null) return true;
		if (GetNodeOrNull<Control>("AgreementOverlay") != null) return true;
		if (_scaleMapDialog != null && _scaleMapDialog.IsVisibleInTree()) return true;
		if (FloatingDialogBase.IsMouseOverAnyDialogOpen(mousePos)) return true;
		return false;
	}

	private bool IsDraggingOrHoveringCoreUI()
	{
		if (HudFade.IsActive) return false;
		if (_minimapController?.IsDragging == true || _isDraggingSlider) return true;

		var hoveredControl = GetViewport().GuiGetHoveredControl();
		if (hoveredControl == null || hoveredControl == this) return false;

		if (hoveredControl == _panelLeft || hoveredControl == _panelRight) return false;

		string[] ignoredNames = { "LeftScroll", "RightScroll", "LeftVBox", "AccordionContainer", "FeedbackLabel", "MapEditorScreenFrame" };
		if (Array.IndexOf(ignoredNames, (string)hoveredControl.Name) >= 0) return false;

		return true;
	}


	private bool AnyWindowVisible(params Window[] windows)
	{
		foreach (var w in windows)
		{
			if (w?.Visible == true) return true;
		}

		return false;
	}

	private bool AnyOptionButtonPopupVisible(params OptionButton?[] buttons)
	{
		foreach (var b in buttons)
		{
			if (b?.GetPopup()?.Visible == true) return true;
		}

		return false;
	}

	private bool IsPanelPopupVisible()
	{
		return AnyWindowVisible(
			_viewportPanel.OverlayPopup,
			_viewportPanel.EnvironmentPopup,
			_cameraPanel.Popup,
			_topToolbar.EditorsPopup,
			_filePanel.SaveMorePopup,
			_filePanel.LoadMorePopup
		);
	}

	private bool IsPrimaryOptionPopupVisible()
	{
		return AnyOptionButtonPopupVisible(
			_topToolbar.ModuleOption,
			_topToolbar.TestSettingsOption,
			_entityPaletteController?.OptCategory,
			_entityPaletteController?.OptCategoryItems,
			_optEyedropperMode,
			_optPathingMode,
			_brushPanel.MirrorOption,
			_pastePanel?.ClipboardMirrorMode
		);
	}

	public void OpenTestSettingsDialog()
	{
		_testSettingsDialog ??= new TestSettingsDialog(this);
		_testSettingsDialog.OpenDialog();
	}

	private bool IsSecondaryOptionPopupVisible()
	{
		return AnyOptionButtonPopupVisible(
			_placementPanel.MirrorOption,
			_viewportPanel.RingSpacingOption,
			_viewportPanel.RadialStepOption,
			_viewportPanel.LightingOption,
			_viewportPanel.WeatherOption
		);
	}

	private bool IsPopupVisible()
	{
		if (_popupRandomGen?.Visible == true) return true;
		if (IsPanelPopupVisible()) return true;
		if (IsPrimaryOptionPopupVisible()) return true;
		if (IsSecondaryOptionPopupVisible()) return true;

		return false;
	}

	/// <summary>
	/// Scrolls the sidebar column under the mouse with the wheel. The columns hold cards that grow to their content
	/// rather than clip it, so a stack taller than the screen is reachable by scrolling it; the wheel is taken here
	/// because the cards themselves cover the column and swallow the event before the scroller can see it.
	/// </summary>
	public bool TryScrollSidebarUnderMouse(InputEventMouseButton wheelEvent)
	{
		if (wheelEvent.ButtonIndex != MouseButton.WheelUp && wheelEvent.ButtonIndex != MouseButton.WheelDown) return false;
		if (GameContext.IsSettingsMenuOpen) return false;
		if (FloatingDialogBase.IsMouseOverAnyDialogOpen(wheelEvent.Position)) return false;

		ScrollContainer? scroller = SidebarScrollerUnderMouse(wheelEvent.Position);
		if (scroller == null || scroller.GetChildCount() == 0) return false;
		if (scroller.GetChild(0) is not Control content) return false;

		if (content.Size.Y <= scroller.Size.Y) return false;

		int step = Mathf.Max(48, Mathf.RoundToInt(scroller.Size.Y * 0.15f));
		scroller.ScrollVertical += wheelEvent.ButtonIndex == MouseButton.WheelDown ? step : -step;
		return true;
	}

	private ScrollContainer? SidebarScrollerUnderMouse(Vector2 mousePosition)
	{
		if (_panelRight != null && _panelRight.IsVisibleInTree() && _panelRight.GetGlobalRect().HasPoint(mousePosition))
		{
			return GetNodeOrNull<ScrollContainer>("RightSlidePanel/RightScroll");
		}

		if (_panelLeft != null && _panelLeft.IsVisibleInTree() && _panelLeft.GetGlobalRect().HasPoint(mousePosition))
		{
			return GetNodeOrNull<ScrollContainer>("LeftSlidePanel/LeftScroll");
		}

		return null;
	}

	private bool IsMouseOverOptionCards(Vector2 mousePos)
	{
		foreach (var card in CardInteraction.Cards)
		{
			if (GodotObject.IsInstanceValid(card) && card.IsVisibleInTree())
			{
				if (card.GetGlobalRect().HasPoint(mousePos)) return true;
			}
		}

		return false;
	}

	private bool IsMouseOverOptionContents(Vector2 mousePos)
	{
		Control[] optionContents = new Control[]
		{
			_filePanel.Content, _viewportPanel.Content, _toolPanel.Content,
			_brushPanel.Content, _contentToolSettings, _placementPanel.Content, _contentInspector, _contentLightingTuning
		};
		foreach (var content in optionContents)
		{
			if (GodotObject.IsInstanceValid(content) && content.IsVisibleInTree())
			{
				if (content.GetGlobalRect().HasPoint(mousePos)) return true;
			}
		}

		return false;
	}

	private bool IsMouseOverToolbarOrOverlays(Vector2 mousePos)
	{
		Control?[] checkControls =
		{
			_btnLeftTab, _btnRightTab,
			GetNodeOrNull<Control>("TopLeftBox"),
			GetNodeOrNull<Control>("BottomCenterBar/TopBar"),
			_middleRightBox, _scaleMapDialog,
			GetNodeOrNull<Control>("GenerationOverlay")
		};

		foreach (var control in checkControls)
		{
			if (control?.Visible == true && control.GetGlobalRect().HasPoint(mousePos)) return true;
		}

		return false;
	}
}
