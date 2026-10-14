using Arch.Core;
using Realm.Ecs.AI.Affordances;

namespace Realm.Ecs.AI.Genres;

public delegate void CustomAffordanceScannerDelegate(World world, int playerIndex, List<GenericAffordance> destinationList, object? customContext);