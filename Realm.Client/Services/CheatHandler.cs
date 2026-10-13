using System.Collections.Generic;
using Arch.Core;
using Realm.Ecs.Services;

namespace Realm.Client.Services;

public delegate (CheatResult Result, int AffectedCount) CheatHandler(CheatService service, Entity playerEntity, DefinitionManager definitionManager, IEnumerable<Entity> selectedEntities);