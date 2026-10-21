with open('Realm.Client/Services/EditorService.cs', 'r') as f:
    content = f.read()

new_method = """	private void ProcessBombingRules<T>(List<T>? rules, Func<T, string> getId, Func<T, float> getDensity, Func<T, float> getMinScale, Func<T, float> getMaxScale, Func<string, Godot.Vector3, Godot.Vector3, float, Node?> spawnFunc, float cellCenterX, float cellCenterZ, float terrainY, float quadSize, List<Node> spawnedList)
	{
		if (rules == null || rules.Count == 0) return;
		foreach (var rule in rules)
		{
			string id = getId(rule);
			if (string.IsNullOrWhiteSpace(id)) continue;
			if (Random.Shared.NextSingle() <= (getDensity(rule) * 0.05f))
			{
				float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
				float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
				float scale = Mathf.Lerp(getMinScale(rule), getMaxScale(rule), Random.Shared.NextSingle());
				float rotY = Random.Shared.NextSingle() * 360f;
				var pos = new Godot.Vector3(cellCenterX + jx, terrainY, cellCenterZ + jz);
				var obj = spawnFunc(id, pos, new Godot.Vector3(0, rotY, 0), scale);
				if (obj != null) spawnedList.Add(obj);
			}
		}
	}
"""

to_replace = """		if (prof.DecalBombingRules != null && prof.DecalBombingRules.Count > 0)
		{
			foreach (var rule in prof.DecalBombingRules)
			{
				if (string.IsNullOrWhiteSpace(rule.DecalId)) continue;
				if (Random.Shared.NextSingle() <= (rule.Density * 0.05f))
				{
					float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
					float rotY = Random.Shared.NextSingle() * 360f;
					var pos = new Godot.Vector3(cellCenterX + jx, terrainY, cellCenterZ + jz);
					var decal = GameHost.Instance?.SpawnDecalExternalWithParams(rule.DecalId, pos, new Godot.Vector3(0, rotY, 0), scale);
					if (decal != null) spawnedList.Add(decal);
				}
			}
		}

		if (prof.VfxBombingRules != null && prof.VfxBombingRules.Count > 0)
		{
			foreach (var rule in prof.VfxBombingRules)
			{
				if (string.IsNullOrWhiteSpace(rule.VfxId)) continue;
				if (Random.Shared.NextSingle() <= (rule.Density * 0.05f))
				{
					float jx = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float jz = (Random.Shared.NextSingle() - 0.5f) * quadSize * 0.8f;
					float scale = Mathf.Lerp(rule.MinScale, rule.MaxScale, Random.Shared.NextSingle());
					float rotY = Random.Shared.NextSingle() * 360f;
					var pos = new Godot.Vector3(cellCenterX + jx, terrainY, cellCenterZ + jz);
					var vfx = GameHost.Instance?.SpawnVfxExternalWithParams(rule.VfxId, pos, new Godot.Vector3(0, rotY, 0), new Godot.Vector3(scale, scale, scale));
					if (vfx != null) spawnedList.Add(vfx);
				}
			}
		}"""

replacement = """		ProcessBombingRules(prof.DecalBombingRules, r => r.DecalId, r => r.Density, r => r.MinScale, r => r.MaxScale, (id, p, r, s) => GameHost.Instance?.SpawnDecalExternalWithParams(id, p, r, s), cellCenterX, cellCenterZ, terrainY, quadSize, spawnedList);
		ProcessBombingRules(prof.VfxBombingRules, r => r.VfxId, r => r.Density, r => r.MinScale, r => r.MaxScale, (id, p, r, s) => GameHost.Instance?.SpawnVfxExternalWithParams(id, p, r, new Godot.Vector3(s, s, s)), cellCenterX, cellCenterZ, terrainY, quadSize, spawnedList);"""

content = content.replace(to_replace, replacement)

# Instead of putting it in the middle of a bunch of methods, append it to the end of the method body of GenerateTerrainProceduralObjects
content = content.replace("		if (spawnedList.Count > 0)\n		{\n			_terrainProceduralObjects[new Vector2I(x, z)] = spawnedList;\n		}\n	}", "		if (spawnedList.Count > 0)\n		{\n			_terrainProceduralObjects[new Vector2I(x, z)] = spawnedList;\n		}\n	}\n\n" + new_method)


with open('Realm.Client/Services/EditorService.cs', 'w') as f:
    f.write(content)
