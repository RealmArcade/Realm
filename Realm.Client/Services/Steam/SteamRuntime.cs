using Godot;

namespace Realm.Client.Services;

[GlobalClass]
public partial class SteamRuntime : Node
{
	public override void _Ready()
	{
		GameContext.Steam.Start(OS.HasFeature("editor"));

		if (GameContext.Steam.RestartRequested)
		{
			GetTree().Quit();
			return;
		}

		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		GameContext.Steam.Pump();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationWMCloseRequest || what == NotificationExitTree) GameContext.Steam.Stop();
	}
}
