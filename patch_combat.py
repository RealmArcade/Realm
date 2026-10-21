with open('Realm.Client/Services/CombatAndDamageService.cs', 'r') as f:
    content = f.read()

new_method = """
		private void EvaluateLocalCandidates(ref AttackerSnapshot attacker, Span<LocalScanCandidate> localCandidates, int candidateCount)
		{
			for (int i = 0; i < candidateCount; i++)
			{
				ref readonly var cand = ref localCandidates[i];
				if (cand.Enemy == Entity.Null) continue;
				if (IsTargetStillAbandoned(attacker.Entity, cand.Enemy)) continue;

				float reach = attacker.AttackRange + cand.TargetCollisionRadius + attacker.TargetCollisionRadius;
				bool isMelee = !attacker.IsFlying && !cand.IsFlying && attacker.AttackRange <= MeleeRangeThreshold;
				float meleeVerticalReach = Math.Max(attacker.AttackRange, DefaultMeleeVerticalReach);
				if (isMelee)
				{
					float contactReach = (attacker.UnitContactRadius + cand.UnitContactRadius)
					                     * GameplayConstants.CollisionSeparationFactor * MeleeContactReachSafetyMargin;
					if (reach < contactReach)
					{
						reach = contactReach;
					}
				}

				if (!IsReachableWithinRangeThreadSafe(attacker.Entity, cand.Enemy, attacker.Position, cand.Position, reach, isMelee, meleeVerticalReach, attacker.IsFlying, attacker.IncludeFlags))
				{
					continue;
				}

				attacker.SelectedTarget = cand.Enemy;
				break;
			}
		}
"""

# add the new method before InsertLocalCandidate
content = content.replace("		private static void InsertLocalCandidate", new_method + "\n		private static void InsertLocalCandidate")

# replace the loop in EvaluateAttackerTargetWithSpatialGrid
to_replace1 = """			for (int i = 0; i < candidateCount; i++)
			{
				ref readonly var cand = ref localCandidates[i];
				if (cand.Enemy == Entity.Null) continue;
				if (IsTargetStillAbandoned(attacker.Entity, cand.Enemy)) continue;

				float reach = attacker.AttackRange + cand.TargetCollisionRadius + attacker.TargetCollisionRadius;
				bool isMelee = !attacker.IsFlying && !cand.IsFlying && attacker.AttackRange <= MeleeRangeThreshold;
				float meleeVerticalReach = Math.Max(attacker.AttackRange, DefaultMeleeVerticalReach);
				if (isMelee)
				{
					float contactReach = (attacker.UnitContactRadius + cand.UnitContactRadius)
										 * GameplayConstants.CollisionSeparationFactor * MeleeContactReachSafetyMargin;
					if (reach < contactReach)
					{
						reach = contactReach;
					}
				}

				if (!IsReachableWithinRangeThreadSafe(attacker.Entity, cand.Enemy, attacker.Position, cand.Position, reach, isMelee, meleeVerticalReach, attacker.IsFlying, attacker.IncludeFlags))
				{
					continue;
				}

				attacker.SelectedTarget = cand.Enemy;
				break;
			}"""
content = content.replace(to_replace1, "			EvaluateLocalCandidates(ref attacker, localCandidates, candidateCount);")

# replace the loop in EvaluateAttackerTarget
to_replace2 = """			for (int i = 0; i < candidateCount; i++)
			{
				ref readonly var cand = ref localCandidates[i];
				if (cand.Enemy == Entity.Null) continue;
				if (IsTargetStillAbandoned(attacker.Entity, cand.Enemy)) continue;

				float reach = attacker.AttackRange + cand.TargetCollisionRadius + attacker.TargetCollisionRadius;
				bool isMelee = !attacker.IsFlying && !cand.IsFlying && attacker.AttackRange <= MeleeRangeThreshold;
				float meleeVerticalReach = Math.Max(attacker.AttackRange, DefaultMeleeVerticalReach);
				if (isMelee)
				{
					float contactReach = (attacker.UnitContactRadius + cand.UnitContactRadius)
					                     * GameplayConstants.CollisionSeparationFactor * MeleeContactReachSafetyMargin;
					if (reach < contactReach)
					{
						reach = contactReach;
					}
				}

				if (!IsReachableWithinRangeThreadSafe(attacker.Entity, cand.Enemy, attacker.Position, cand.Position, reach, isMelee, meleeVerticalReach, attacker.IsFlying, attacker.IncludeFlags))
				{
					continue;
				}

				attacker.SelectedTarget = cand.Enemy;
				break;
			}"""
content = content.replace(to_replace2, "			EvaluateLocalCandidates(ref attacker, localCandidates, candidateCount);")

with open('Realm.Client/Services/CombatAndDamageService.cs', 'w') as f:
    f.write(content)
