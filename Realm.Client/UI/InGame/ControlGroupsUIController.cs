using Godot;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.UI.InGame;

public class ControlGroupsUIController
{
	private readonly HBoxContainer _container;
	private readonly Button[] _buttons = new Button[10];

	public ControlGroupsUIController(HBoxContainer container)
	{
		_container = container;

		for (int i = 0; i < 10; i++)
		{
			var btn = new Button();
			btn.Name = "Btn_Button";
			btn.Text = $"{i}";
			btn.CustomMinimumSize = new Vector2(30, 30);
			btn.FocusMode = Control.FocusModeEnum.None;
			btn.AddThemeStyleboxOverride("normal", UIStyle.CreateHUDButtonStyle(false, false));
			btn.AddThemeStyleboxOverride("hover", UIStyle.CreateHUDButtonStyle(true, false));
			btn.AddThemeStyleboxOverride("pressed", UIStyle.CreateHUDButtonStyle(false, true));

			int groupIndex = i;
			btn.Pressed += () => GameContext.Host?.RecallControlGroup(groupIndex);
			btn.Visible = false;
			_container.AddChild(btn);
			_buttons[i] = btn;
		}
	}

	public void Update()
	{
		if (GameContext.Host == null)
		{
			return;
		}

		for (int i = 0; i < 10; i++)
		{
			var groupUnits = GameContext.Host._controlGroups[i];
			bool hasUnits = groupUnits != null && groupUnits.Count > 0;
			if (_buttons[i].Visible != hasUnits)
			{
				_buttons[i].Visible = hasUnits;
			}
		}
	}
}