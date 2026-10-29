using Godot;
using Realm.Ecs;

namespace Realm.Client.UI.InGame;

[GlobalClass]
public partial class CommandCardButton : Button
{
	public TooltipContext TooltipContext = TooltipContext.ForUnit(Entity.Null);

	public override Control _MakeCustomTooltip(string forText)
	{
		return RichTooltip.Create(forText, TooltipContext);
	}
}
