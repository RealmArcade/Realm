using Arch.Core;
using Realm.Ecs.AI.Affordances;
using System.Numerics;

namespace Realm.Ecs.AI.Genres;

public delegate void CustomActionExecutorDelegate(World world, int playerIndex, in GenericAffordance affordance, Action<int, string, Vector3, string>? customActionCallback);