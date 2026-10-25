using Godot;
using Realm.Ecs;

namespace Realm.Client.UI.InGame;

[GlobalClass]
public partial class CommandCardButton : Button
{
	/// <summary>
	/// What this card's tooltip can read. The card keeps the template in <c>TooltipText</c> and this
	/// context alongside it, so the text is produced on hover and shows current numbers.
	/// </summary>
	public TooltipContext TooltipContext = TooltipContext.ForUnit(Entity.Null);

	public override Control _MakeCustomTooltip(string forText)
	{
		return RichTooltip.Create(forText, TooltipContext);
	}
}
