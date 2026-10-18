using System.Numerics;
using Realm.Ecs.AI.Affordances;

namespace Realm.Ecs.AI.Genres;

public delegate void CustomActionExecutorDelegate(EcsWorld world, int playerIndex, in GenericAffordance affordance, Action<int, string, Vector3, string>? customActionCallback);