using Realm.Client.Services;

namespace Realm.Client.Core;

public partial class GameHost
{
	public override void _EnterTree()
	{
		ViewLogic_EnterTree();
	}

	private void ViewLogic_EnterTree()
	{
		ResolveServices();
	}

	private void ResolveServices()
	{
		GameContext.EnsureServices();
	}
}