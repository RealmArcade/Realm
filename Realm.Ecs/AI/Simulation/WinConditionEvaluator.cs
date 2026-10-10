using Arch.Core;

namespace Realm.Ecs.AI.Simulation;

public delegate int? WinConditionEvaluator(World world, float matchDurationSeconds);