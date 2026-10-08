using Arch.Core;
using System;
using System.Numerics;

namespace Realm.Ecs.AI.Affordances;

public interface IActionExecutor
{
	void ExecuteAction(World world, int playerIndex, in GenericAffordance affordance, Action<int, string, Vector3, string>? customActionCallback = null);
}
