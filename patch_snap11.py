import sys

with open('Realm.Client/Services/UnitSpawnService.cs', 'r') as f:
    spawn = f.read()

spawn = spawn.replace("ref Realm.Ecs.Components.Meta.NetworkMappingState", "ref Realm.Ecs.Components.Core.NetworkMappingState")

with open('Realm.Client/Services/UnitSpawnService.cs', 'w') as f:
    f.write(spawn)

