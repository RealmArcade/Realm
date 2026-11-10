using System;
using System.Collections.Generic;
using Godot;
using Realm.Client.Services;
using Realm.Client.Utils;

namespace Realm.Client.UI.MapEditor;

/// <summary>
/// What card interaction needs from the editor: the node it looks the two sidebar columns up in, and the feedback line
/// the reset and double-click gestures report through.
/// </summary>
public sealed record CardInteractionHooks(
	Node Owner,
	Action<string> Feedback);

/// <summary>
/// Dragging and collapsing the editor's cards. A card is dragged by its header: a press starts the gesture, movement
/// past a small threshold lifts the card out of its column, and a release without movement is read as a click that
/// collapses or expands the card instead. Double-clicking a header puts that card back in its column.
/// </summary>
public sealed class MapEditorCardInteraction
{
	private const float DragThresholdSquared = 16.0f;

	private readonly CardInteractionHooks _hooks;
	private readonly Dictionary<Control, CardDragData> _cards = new();

	public MapEditorCardInteraction(CardInteractionHooks hooks)
	{
		_hooks = hooks;
	}

	/// <summary>The cards that have been made draggable, in registration order.</summary>
	public IReadOnlyCollection<Control> Cards => _cards.Keys;

	public void Register(Control cardNode, Button headerBtn, Control? contentControl = null, string? titleText = null)
	{
		if (cardNode == null || headerBtn == null) return;

		var data = new CardDragData
		{
			CardNode = cardNode,
			HeaderButton = headerBtn,
			ContentControl = contentControl,
			TitleText = titleText
		};
		_cards[cardNode] = data;

		headerBtn.GuiInput += (@event) => HandleGuiInput(@event, cardNode, headerBtn, contentControl, titleText, data);
	}

	/// <summary>Puts every card back in its column, which is what the toolbar's reset-layout button asks for.</summary>
	public void ResetAll()
	{
		foreach (var card in new List<Control>(_cards.Keys))
		{
			ResetCard(card);
		}

		SortColumn("LeftSlidePanel/LeftScroll/LeftVBox");
		SortColumn("RightSlidePanel/RightScroll/AccordionContainer");

		_hooks.Feedback(TranslationServer.Translate("All panel positions reset to default layout."));
	}

	/// <summary>Opens a card that some other action needs visible, without closing one that is already open.</summary>
	public void Expand(Button headerBtn, Control contentControl, string titleText)
	{
		if (headerBtn == null || contentControl == null) return;
		if (contentControl.Visible) return;
		Toggle(headerBtn, contentControl, titleText);
	}

	public void Toggle(Button headerBtn, Control? contentControl, string? titleText)
	{
		if (headerBtn == null || contentControl == null) return;
		contentControl.Visible = !contentControl.Visible;
		if (!string.IsNullOrEmpty(titleText))
		{
			string upperTitle = TranslationServer.Translate(titleText).ToString().ToUpperInvariant();
			headerBtn.Text = upperTitle + (contentControl.Visible ? $"  {UnicodeIcons.CARET_DOWN}" : $"  {UnicodeIcons.CARET_RIGHT}");
		}

		SortCardContainers(headerBtn);
		SortColumns();

		GameContext.UserInterface?.PlayClickSound();
	}

	public void ResetCard(Control? cardNode)
	{
		if (cardNode == null) return;

		if (_cards.TryGetValue(cardNode, out var data))
		{
			data.IsDragging = false;
			data.HasMovedSincePress = false;
		}

		cardNode.TopLevel = false;
		cardNode.Position = Vector2.Zero;

		(cardNode.GetParent() as Container)?.QueueSort();

		SortColumn("LeftSlidePanel/LeftScroll/LeftVBox");
		SortColumn("RightSlidePanel/RightScroll/AccordionContainer");
	}

	private void HandleGuiInput(InputEvent @event, Control cardNode, Button headerBtn, Control? contentControl, string? titleText, CardDragData data)
	{
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
		{
			HandleMouseButton(mb, cardNode, headerBtn, contentControl, titleText, data);
		}
		else if (@event is InputEventMouseMotion mm && data.IsDragging)
		{
			HandleMouseMotion(mm, cardNode, data);
		}
	}

	private void HandleMouseButton(InputEventMouseButton mb, Control cardNode, Button headerBtn, Control? contentControl, string? titleText, CardDragData data)
	{
		if (mb.Pressed)
		{
			if (mb.DoubleClick)
			{
				data.IsDragging = false;
				data.HasMovedSincePress = false;
				ResetCard(cardNode);
				_hooks.Feedback(TranslationServer.Translate("Card position reset to panel layout."));
				return;
			}

			data.IsDragging = true;
			data.HasMovedSincePress = false;
			data.DragStartMousePos = mb.GlobalPosition;
			data.CardStartPos = cardNode.GlobalPosition;
		}
		else
		{
			if (data.IsDragging)
			{
				bool moved = data.HasMovedSincePress;
				data.IsDragging = false;
				data.HasMovedSincePress = false;

				if (!moved && contentControl != null)
				{
					Toggle(headerBtn, contentControl, titleText);
				}
			}
		}
	}

	private static void HandleMouseMotion(InputEventMouseMotion mm, Control cardNode, CardDragData data)
	{
		Vector2 delta = mm.GlobalPosition - data.DragStartMousePos;
		if (!data.HasMovedSincePress && delta.LengthSquared() > DragThresholdSquared)
		{
			data.HasMovedSincePress = true;
			if (!cardNode.TopLevel)
			{
				cardNode.TopLevel = true;
			}
		}

		if (data.HasMovedSincePress)
		{
			if (!cardNode.TopLevel)
			{
				cardNode.TopLevel = true;
			}

			cardNode.GlobalPosition = data.CardStartPos + delta;
		}
	}

	/// <summary>
	/// Re-sorts the card's own container and then the column around it, because collapsing a card changes the height of
	/// both and Godot does not re-run the layout until the next frame otherwise.
	/// </summary>
	private static void SortCardContainers(Button headerBtn)
	{
		var cardParent = headerBtn.GetParent() as Control;
		if (cardParent == null) return;

		cardParent.ForceUpdateTransform();
		(cardParent as Container)?.QueueSort();

		var sidebarContainer = cardParent.GetParent() as Container;
		if (sidebarContainer != null)
		{
			sidebarContainer.ForceUpdateTransform();
			sidebarContainer.QueueSort();
		}
	}

	private void SortColumns()
	{
		SortColumn("LeftSlidePanel/LeftScroll/LeftVBox");
		SortColumn("RightSlidePanel/RightScroll/AccordionContainer");
	}

	private void SortColumn(string path)
	{
		var column = _hooks.Owner.GetNodeOrNull<VBoxContainer>(path);
		column?.ForceUpdateTransform();
		column?.QueueSort();
	}
}
