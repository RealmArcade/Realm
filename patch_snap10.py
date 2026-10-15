import sys
import re

with open('Realm.Client/Services/UnitSpawnService.cs', 'r') as f:
    spawn = f.read()

# Replace ref Realm.Client.Core.NetworkMappingState with ref NetworkMappingState.
# Wait, NetworkMappingState is in Realm.Ecs.Components.Meta probably.
spawn = spawn.replace("ref Realm.Client.Core.NetworkMappingState", "ref Realm.Ecs.Components.Meta.NetworkMappingState")

with open('Realm.Client/Services/UnitSpawnService.cs', 'w') as f:
    f.write(spawn)

