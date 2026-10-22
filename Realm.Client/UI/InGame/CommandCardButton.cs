using Godot;

namespace Realm.Client.UI.InGame;

[GlobalClass]
public partial class CommandCardButton : Button
{
	public override Control _MakeCustomTooltip(string forText)
	{
		return RichTooltip.Create(forText);
	}
}