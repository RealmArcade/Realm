with open('Realm.Client/UI/MapEditor/TemplateManagerDialog.cs', 'r') as f:
    content = f.read()

new_method = """	private void RemoveKeysFromDict<T>(Dictionary<string, T>? dict, string category, ObjectItemInfo item, string delSlug)
	{
		if (dict == null) return;
		var keysToRemove = dict.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(delSlug) && (string.Equals(k, $"{delSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), delSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID(category, k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			dict.Remove(k);
		}
	}
"""

to_replace1 = """	private void RemoveTerrainTexture(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		if (meta.Textures == null) return;
		var keysToRemove = meta.Textures.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(delSlug) && (string.Equals(k, $"{delSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), delSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("terrain", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			meta.Textures.Remove(k);
		}
	}"""
content = content.replace(to_replace1, new_method + "\n\tprivate void RemoveTerrainTexture(MapMetadata meta, ObjectItemInfo item, string delSlug)\n\t{\n\t\tRemoveKeysFromDict(meta.Textures, \"terrain\", item, delSlug);\n\t}")

to_replace2 = """	private void RemoveSpritesheet(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		if (meta.VfxSpritesheets == null) return;
		var keysToRemove = meta.VfxSpritesheets.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(delSlug) && (string.Equals(k, $"{delSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), delSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("spritesheet", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			meta.VfxSpritesheets.Remove(k);
		}
	}"""
content = content.replace(to_replace2, "\tprivate void RemoveSpritesheet(MapMetadata meta, ObjectItemInfo item, string delSlug)\n\t{\n\t\tRemoveKeysFromDict(meta.VfxSpritesheets, \"spritesheet\", item, delSlug);\n\t}")

to_replace3 = """	private void RemoveDecal(MapMetadata meta, ObjectItemInfo item, string delSlug)
	{
		if (meta.Decals == null) return;
		var keysToRemove = meta.Decals.Keys.Where(k =>
			string.Equals(k, item.TemplateID, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(k, delSlug, StringComparison.OrdinalIgnoreCase) ||
			(!string.IsNullOrEmpty(item.ModelPath) && string.Equals(k, item.ModelPath, StringComparison.OrdinalIgnoreCase)) ||
			(!string.IsNullOrEmpty(delSlug) && (string.Equals(k, $"{delSlug}.rtex", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetFileNameWithoutExtension(k), delSlug, StringComparison.OrdinalIgnoreCase))) ||
			string.Equals(TemplateIDHelper.NormalizeTemplateID("decal", k), item.TemplateID, StringComparison.OrdinalIgnoreCase)
		).ToList();

		foreach (var k in keysToRemove)
		{
			meta.Decals.Remove(k);
		}
	}"""
content = content.replace(to_replace3, "\tprivate void RemoveDecal(MapMetadata meta, ObjectItemInfo item, string delSlug)\n\t{\n\t\tRemoveKeysFromDict(meta.Decals, \"decal\", item, delSlug);\n\t}")

with open('Realm.Client/UI/MapEditor/TemplateManagerDialog.cs', 'w') as f:
    f.write(content)
