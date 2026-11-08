// Module switch button state and the left/right panel expand-collapse pair: the accordion hooks the
// collapsing panels reuse, and the two tweens that slide them.

using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using Realm.Client.Core;
using Realm.Client.Network;
using Realm.Client.Services;
using Realm.Client.Services.MapHistory;
using Realm.Client.UI.MapEditor;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Shared;
using Realm.Shared.Distribution;
using Realm.Shared.Services;
using Environment = System.Environment;
using Key = NSec.Cryptography.Key;
using MirrorMode = Realm.Ecs.Components.Core.MirrorMode;
using WaterType = Realm.Ecs.Components.Terrain.WaterType;
using static Godot.Control;
using Realm.Client.Utils;


namespace Realm.Client.UI
{
	public partial class MapEditorHUD
	{
		private void UpdateModuleSwitchButtons()
		{
			if (_topToolbar.ModuleOption != null)
			{
				_topToolbar.ModuleOption.Selected = (int)_activeModule;
			}
		}


		private void SetupAccordion(Button headerBtn, Control contentControl, string titleText)
		{
			string upperTitle = TranslationServer.Translate(titleText).ToString().ToUpperInvariant();
			headerBtn.Text = upperTitle + (contentControl.Visible ? $"  {UnicodeIcons.CARET_DOWN}" : $"  {UnicodeIcons.CARET_RIGHT}");
			var font = GetFontAwesomeFont();
			if (font != null)
			{
				headerBtn.AddThemeFontOverride("font", font);
			}

			StyleSubContainer(contentControl);
		}

		private void StyleRowButton(Button btn) => MapEditorStyle.RowButton(btn);

		private Control? GetContentTarget(Control? contentControl) => MapEditorStyle.ContentTarget(contentControl);

		private void StyleSubContainer(Control boxNode, string? headerText = null) => MapEditorStyle.SubContainer(boxNode, headerText);

		private void StyleCheckBoxRow(CheckBox chk) => MapEditorStyle.CheckBoxRow(chk);

		private void StyleMapEditorTopButton(Button btn) => MapEditorStyle.TopButton(btn);

		private void StyleCompactMapEditorTopButton(Button btn, int horizontalMargin = 4) =>
			MapEditorStyle.TopButton(btn, horizontalMargin);


		private MapEditorCardInteraction? _cardInteraction;

		/// <summary>Dragging, collapsing and resetting the sidebar cards.</summary>
		internal MapEditorCardInteraction CardInteraction =>
			_cardInteraction ??= new MapEditorCardInteraction(new CardInteractionHooks(this, ShowFeedback));

		private void MakeCardDraggable(Control cardNode, Button headerBtn, Control? contentControl = null, string? titleText = null) =>
			CardInteraction.Register(cardNode, headerBtn, contentControl, titleText);

		private void ExpandAccordion(Button headerBtn, Control contentControl, string titleText) =>
			CardInteraction.Expand(headerBtn, contentControl, titleText);

		public void ResetAllPanelPositions() => CardInteraction.ResetAll();

		private void ToggleLeftPanel()
		{
			SetLeftPanelExpanded(!_leftPanelExpanded);
		}

		private void ToggleRightPanel()
		{
			SetRightPanelExpanded(!_rightPanelExpanded);
		}

		private void SetLeftPanelExpanded(bool expand)
		{
			_leftPanelExpanded = expand;
			var tween = CreateTween();
			float targetLeft = expand ? 0.0f : -260.0f;
			float targetRight = expand ? 260.0f : 0.0f;
			tween.TweenProperty(_panelLeft, "offset_left", targetLeft, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(_panelLeft, "offset_right", targetRight, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			_btnLeftTab.Text = expand ? "◀" : "▶";
		}

		private void SetRightPanelExpanded(bool expand)
		{
			_rightPanelExpanded = expand;
			var tween = CreateTween();
			float targetLeft = expand ? -300.0f : 0.0f;
			float targetRight = expand ? 0.0f : 300.0f;
			tween.TweenProperty(_panelRight, "offset_left", targetLeft, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(_panelRight, "offset_right", targetRight, 0.2f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
			_btnRightTab.Text = expand ? "▶" : "◀";
		}
	}
}
