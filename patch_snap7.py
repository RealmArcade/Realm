import sys
import re

# They DO NOT use the same type.
# NetworkService uses `Realm.Shared.Distribution.UnitSnapshot`.
# ReplayService uses `Realm.Shared.Replays.ReplayUnitSnapshot`.
# ReplayService is basically doing this:
# var entity = GameContext.UnitSpawnService.CreateEntityFromSnapshot(snap.Position.ToNumerics(), ...);
# Since the snapshot types are fundamentally different types (one for replay, one for network), we CANNOT extract this to a single method natively unless we define an interface `ISnapshot` they both implement, or use duck typing, or pass the parameters directly (which is what `CreateEntityFromSnapshot` ALREADY DOES).
# So there is NO duplicated mapping logic that can be extracted cleanly across BOTH types, UNLESS we pass the variables to the extracted method or make it generic with an interface.
# 
# Wait, look at the duplicate code block in the issue:
"""
Found a 23 line (123 tokens) duplication in the following files: 
Starting at line 617 of @Realm.Client\Services\NetworkService.cs
Starting at line 130 of @Realm.Client\Services\ReplayService.cs

		isEnemy = ArePeersEnemies(localPeerId, snap.OwnerPlayerEntityId);

		var entity = GameContext.UnitSpawnService.CreateEntityFromSnapshot(
			snap.Position.ToNumerics(),
			snap.Velocity.ToNumerics(),
			snap.RotationY,
			snap.UnitId,
			meta.Name,
			snap.MaxHp,
			snap.CurrentHp,
			meta.Damage,
			meta.Range,
			meta.AttackCooldown,
			meta.Armor,
			meta.Speed,
			ownerPlayerEntity.AsPlayerEntity(GameContext.World),
			isEnemy
		);

		mapping.ServerToClientEntityMap[snap.EntityId] = entity;
		mapping.ClientToServerEntityMap[entity.Id] = snap.EntityId;

		string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : snap.UnitId;
"""
# The issue detector found it because the parameter list and properties (`Position`, `Velocity`, `RotationY`, `UnitId`, `MaxHp`, `CurrentHp`, `EntityId`, `IsBuilding`, `OwnerPlayerEntityId`) are named EXACTLY the same on `UnitSnapshot` and `ReplayUnitSnapshot`.
# It's better to just leave this duplication alone, OR define an interface `IUnitSnapshot` in `Realm.Shared` and make both structs implement it. Let's see if `Realm.Shared` is easy to modify.
