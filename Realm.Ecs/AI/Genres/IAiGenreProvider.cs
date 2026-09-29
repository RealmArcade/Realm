using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Realm.Ecs.AI.Genres;

public interface IAiGenreProvider : IAffordanceScanner, IActionExecutor
{
	string GenreName { get; }
	float[] GetDefaultWeights();
	BotProfile CreateDefaultProfile(string mapName);
	void ConfigureFromParameters(IReadOnlyDictionary<string, string> parameters);
}
