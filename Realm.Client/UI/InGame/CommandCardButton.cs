using Godot;

namespace Realm.Client.UI.InGame;

public partial class CommandCardButton : Button
{
	public override Control _MakeCustomTooltip(string forText)
	{
		return RichTooltip.Create(forText);
	}
}