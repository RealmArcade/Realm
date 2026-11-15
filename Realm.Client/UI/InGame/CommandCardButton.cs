using System;
using Godot;

namespace Realm.Client.UI.InGame;

[GlobalClass]
public partial class CommandCardButton : Button
{
	public TooltipContext TooltipContext = TooltipContext.ForUnit(Realm.Ecs.Entity.Null);

	/// <summary>
	///     Raised when the player right-clicks the card, which is how an auto-cast ability is switched on or off. A plain
	///     press stays the cast the card already describes.
	/// </summary>
	public event Action? RightClicked;

	public override Control _MakeCustomTooltip(string forText)
	{
		return RichTooltip.Create(forText, TooltipContext);
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) return;

		RightClicked?.Invoke();
		AcceptEvent();
	}
}
