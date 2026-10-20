using System.Collections.Generic;
using Realm.Ecs;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

public delegate (CheatResult Result, int AffectedCount) CheatHandler(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities);