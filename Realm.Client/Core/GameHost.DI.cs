using Arch.Core;
using Microsoft.Extensions.DependencyInjection;
using Realm.Ecs.Services;
using Realm.EditorAPI;
using Realm.Client;
using Realm.Client.Core;
using Realm.Client.Services;

namespace Realm.Client.Core;

public partial class GameHost
{
	public override void _EnterTree()
	{
		ResolveServices();
	}

	private void ResolveServices()
	{
		ServiceLocator.EnsureServices();


	}
}
