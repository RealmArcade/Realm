using Godot;
using System;
using System.Collections.Generic;

namespace Realm.Client.UI.InGame;

/// <summary>
///     The player's per-element HUD switches. The map of wishes lives here, and so does the one place that knows what each
///     element's wish means for the nodes on screen — which is why adding an element is a change to one switch rather than
///     a search for every place that fences a panel.
/// </summary>
public sealed class HudVisibility
{
	private readonly Dictionary<HudElement, bool> _wishes = new();

	private readonly Func<Control?> _minimapFrame;
	private readonly Func<Control?> _minimapControls;
	private readonly Func<Control?> _resourceBar;
	private readonly Func<Control?> _commandCard;
	private readonly Func<Control?> _portrait;
	private readonly Func<Control?> _chat;
	private readonly Func<Control?> _inventoryFrame;
	private readonly Func<Control?> _inventoryGrid;
	private readonly Action _refreshCommandCard;
	private readonly Action _refreshCountdown;
	private readonly Action _refreshScoreboard;

	public HudVisibility(
		Func<Control?> minimapFrame,
		Func<Control?> minimapControls,
		Func<Control?> resourceBar,
		Func<Control?> commandCard,
		Func<Control?> portrait,
		Func<Control?> chat,
		Func<Control?> inventoryFrame,
		Func<Control?> inventoryGrid,
		Action refreshCommandCard,
		Action refreshCountdown,
		Action refreshScoreboard)
	{
		_minimapFrame = minimapFrame;
		_minimapControls = minimapControls;
		_resourceBar = resourceBar;
		_commandCard = commandCard;
		_portrait = portrait;
		_chat = chat;
		_inventoryFrame = inventoryFrame;
		_inventoryGrid = inventoryGrid;
		_refreshCommandCard = refreshCommandCard;
		_refreshCountdown = refreshCountdown;
		_refreshScoreboard = refreshScoreboard;
	}

	public bool IsVisible(HudElement element, bool defaultValue = true) =>
		_wishes.TryGetValue(element, out bool wish) ? wish : defaultValue;

	/// <summary>Records the wish without acting on it; <see cref="Apply" /> is what touches the nodes.</summary>
	public void Set(HudElement element, bool visible) => _wishes[element] = visible;

	public void Apply(HudElement element)
	{
		bool visible = IsVisible(element, true);
		switch (element)
		{
			case HudElement.Minimap:
				if (_minimapFrame() is { } minimapFrame) minimapFrame.Visible = visible;
				if (_minimapControls() is { } minimapControls) minimapControls.Visible = visible;
				break;
			case HudElement.ResourceBar:
				if (_resourceBar() is { } resourceBar) resourceBar.Visible = visible;
				break;
			case HudElement.CommandCard:
				if (!visible && _commandCard() is { } commandCard)
				{
					commandCard.Visible = false;
				}
				else if (visible)
				{
					// The command card's contents depend on the selection, so reopening it re-asks for them.
					_refreshCommandCard();
				}
				break;
			case HudElement.UnitPortrait:
				if (_portrait() is { } portrait) portrait.Visible = visible;
				break;
			case HudElement.Chat:
				if (_chat() is { } chat) chat.Visible = visible;
				break;
			case HudElement.Leaderboard:
			case HudElement.SummaryTable:
				_refreshScoreboard();
				break;
			case HudElement.Inventory:
				if (_inventoryFrame() is { } inventoryFrame) inventoryFrame.Visible = visible;
				if (_inventoryGrid() is { } inventoryGrid) inventoryGrid.Visible = visible;
				break;
			case HudElement.CountdownTimer:
				_refreshCountdown();
				break;
		}
	}

	/// <summary>
	///     Puts back the fences after a refresh has shown a panel again on its own account. It only ever hides: a refresh
	///     that legitimately wants a panel gone has already said so, and a wish to show is applied by <see cref="Apply" />.
	/// </summary>
	public void HideDisallowed()
	{
		if (!IsVisible(HudElement.UnitPortrait, true) && _portrait() is { } portrait) portrait.Visible = false;
		if (!IsVisible(HudElement.CommandCard, true) && _commandCard() is { } commandCard) commandCard.Visible = false;
		if (!IsVisible(HudElement.Minimap, true) && _minimapFrame() is { } minimapFrame) minimapFrame.Visible = false;
		if (!IsVisible(HudElement.ResourceBar, true) && _resourceBar() is { } resourceBar) resourceBar.Visible = false;
		if (!IsVisible(HudElement.Inventory, true))
		{
			if (_inventoryFrame() is { } inventoryFrame) inventoryFrame.Visible = false;
			if (_inventoryGrid() is { } inventoryGrid) inventoryGrid.Visible = false;
		}
	}
}
