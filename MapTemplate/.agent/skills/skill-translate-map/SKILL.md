---
name: skill-translate-map
description: Extracts, translates, verifies and prunes this map's own locale/*.json catalogs so metadata.json names and ability tooltips render in each player's chosen language, without touching the Realm client's catalogs.
---

# Skill: Translate Map (skill-translate-map)

## Trigger Conditions
Activates whenever the work is about this **map's** text in another language:
- "translate this map to Spanish", "add French tooltips", "localize the ability descriptions".
- Filling in or repairing `locale/*.json` in this map folder.
- Auditing the map for text that is still English when the player runs it in another language.

## Scope: map text only
- **Translate:** `metadata.json` template text the client renders — template `Name` and `Description` for units, buildings, abilities, items and upgrades — plus any key this map's script passes to `api.Translate`.
- **Never edit the Realm client's catalogs.** `Realm.Client/locale/*.json` in the engine repository is engine-screen text (menus, HUD, editor). It is maintained by the engine's own `skill-update-translations` skill, it is not part of this map, and changing it from a map workspace changes nothing about this map.
- **Never translate:** template ids (`ability/…`, `item/…`), asset paths, icon paths, class names, or catalog keys. `MapProperties.MapTitle`/`MapDescription` are shown in the lobby before the map loads, so translating them here has no effect.

## How map localization works
- A map ships one flat catalog per language in its own folder: `locale/es.json`, `locale/fr.json`, … shaped `{"English text": "translated text"}`.
- The client merges the map's catalog over the engine's catalogs when the map loads and renders text in the language chosen in **Settings → Game → Language**. The lookup happens on each client, so every player in a match reads the map in their own language; nothing runs in the map's WASM.
- A missing entry simply reads as the English text, so a **partial catalog is valid**: a map can ship one translated tooltip or a whole language. **No `en.json` is needed** — add one only to deliberately change the English wording.
- **Keys are the English text itself**, which is why there is no key naming scheme to learn:
  - `Name` → keyed **without** its BBCode. `"[color=#FF0000]Seismic Rupture[/color]"` is translated by an entry keyed `"Seismic Rupture"`; the colour stays in `metadata.json` and the translation is plain text.
  - `Description` → keyed by the **whole string as written**, `{{tokens}}` included. The client looks the string up first and fills the tokens in afterwards, so a translation may move placeholders wherever the target language wants them.
  - Ability `Role` (`"Damage"`) → a key of its own, printed in front of the tooltip as `[Damage]`. Translate it to translate that tag.
  - If a description already carries its own prefix (`"[Damage] Deals …"`), key either the whole line or the two parts (`"Damage"` and `"Deals …"`); the client accepts both.
- Translations travel inside the map package: `locale/*.json` is picked up by the manifest on the next save/export and is never trimmed out of an `.rmap`. No manifest edit is required.
- Only the languages the client ships are loaded: `en`, `es`, `fr`, `de`, `pt`, `ru`, `zh`, `ja`, `ar` (right-to-left, handled by the client), `hi`, `ko`. A file named for any other code is ignored.

## Invariants
1. **Keys are copied, never rewritten.** A key must match the source text character for character — punctuation, casing, apostrophes, and the text after BBCode removal for names.
2. **Placeholders survive.** Keep every `{{Scope.Key}}` token (and any `{0}` a script formats itself) exactly as written, and keep the same set. Reordering is allowed; renaming, translating, dropping or adding tokens is not.
3. **BBCode survives.** `[b]`, `[i]`, `[color=#RRGGBB]`, `[img=16x16]path[/img]`, `[list]` … keep their tags, attributes and paths; translate only the visible text. The one exception is names, whose markup stays in `metadata.json`, so their catalog entries carry no tags.
4. **No invented keys.** A key that no longer exists in `metadata.json` is an orphan; `prune` removes it.
5. **No invented content.** Translate what the map says; never add mechanics, numbers, rules or flavour that the English text does not contain.
6. **Casing follows the source.** A Title Case name stays Title Case; an ALL CAPS label stays ALL CAPS.

## Workflow
Run every command from the map folder (the directory holding `metadata.json`). The helper script is next to this file; use `py` or `python3` instead of `python` if that is what the machine provides.

1. **See where the map stands**
   ```bash
   python .agent/skills/skill-translate-map/verify_keys.py verify
   ```
   Lists the required keys and reports missing entries, empty values, placeholder/BBCode mismatches and orphans for every `locale/*.json`.
2. **Get a batch of just the untranslated strings**
   ```bash
   python .agent/skills/skill-translate-map/verify_keys.py list --locale es > batch.es.json
   ```
   The file maps every outstanding key to itself; replace each value with the translation. It is scratch input for the next
   step: keep it outside the map or delete it once merged, because a stray `batch.*.json` in the map folder is scanned into
   the manifest like any other file.
3. **Translate the values** in batches of roughly 30–60 entries, one language at a time, following the prompt contract below. Do not translate the keys.
4. **Merge the batch into the catalog**
   ```bash
   python .agent/skills/skill-translate-map/verify_keys.py merge es batch.es.json
   ```
   Writes `locale/es.json` sorted by key so later diffs stay readable. Repeat for each language. Delete the batch files afterwards.
5. **Verify until clean**
   ```bash
   python .agent/skills/skill-translate-map/verify_keys.py verify
   ```
   Exit code 0 means every catalog is valid.
6. **Prune after renaming or deleting templates**
   ```bash
   python .agent/skills/skill-translate-map/verify_keys.py prune
   ```
   Refuses to run when more than half of a catalog would disappear, which is the sign that `metadata.json` was not the file being checked.
7. **Check it in the game.** Start the map (Play, or Test Map from the editor), open **Settings → Game → Language**, and hover an ability card, open the build menu and the blacksmith shop. Every one of them must read in the chosen language, with its `{{tokens}}` still filled in and its colour styling intact.

## Master Translation Prompt Contract
When delegating a batch to an LLM, enforce this contract:

```markdown
You are localizing a user-made map for the real-time strategy game "Realm". The map is an arena/RTS arcade map.
Translate the following JSON dictionary of English strings into {{TargetLanguage}} (locale: {{LocaleCode}}).
The keys are the game's own source text and are how the game finds the translation.

### Mandatory Rules:
1. **Identical Keys**: output the keys exactly as given, character for character. Never translate or reformat a key.
2. **Translate Values Only**: natural, idiomatic wording a strategy player expects in tooltips and unit names.
3. **Placeholder Preservation**: keep every `{{...}}` token (for example `{{Ability.Effect0.Amount}}`) and every `{0}` exactly intact. The same tokens must appear at least once in the value; they may be reordered to fit the sentence.
4. **BBCode Preservation**: never modify, drop or add BBCode tags: `[b]`, `[/b]`, `[i]`, `[color=#RRGGBB]`, `[img=16x16]path[/img]`, `[list]`, `[*]`, `[url]`. Translate only the text between tags.
5. **Numbers and Gameplay Terms**: keep numbers, percentages, seconds, resource names and stat names consistent with the rest of the batch.
6. **Casing Matching**: Title Case names stay Title Case, ALL CAPS stays ALL CAPS, sentence case stays sentence case.
7. **No Inventions**: do not add rules, values or flavour that the English text does not contain.
8. **Output**: return only valid JSON with the same structure as the input.
```

## Verification Rules
`verify_keys.py verify` mirrors the engine's own translation checks, adapted to map catalogs:
- **Missing keys** — a required name or description with no entry and no valid alternative (the split role + sentence form).
- **Empty values** — present but blank.
- **Placeholder mismatches** — the set of `{{...}}` (and `{0}`) tokens differs from the English text.
- **BBCode tag mismatches** — the set of tags differs from the English text.
- **Bracket hint mismatches** — other bracketed text such as `[Damage]` or `[Q]` was dropped or reworded.
- **Orphan keys** — an entry whose English text no longer exists in `metadata.json`.
- **Values identical to English** — a warning, not a failure; proper nouns stay as they are.

## Troubleshooting
- **Everything is still English.** The catalog has to sit in `<map folder>/locale/<code>.json` with the code spelled as the client uses it. Reload the match after adding it; the client reads the catalogs when the map loads.
- **One tooltip is still English.** Compare the key against `metadata.json` exactly — trailing punctuation, casing and apostrophes all count. For a name, remember the key has no BBCode.
- **A tooltip shows a red `{{Ability.…}}`.** The map script has not published that tooltip value, so there is nothing to substitute. That is a map-script issue (`SetAbilityTooltipValues`), not a translation defect.
- **`prune` refuses to run.** It would delete more than half of the catalog, which usually means `metadata.json` is missing or being read from the wrong folder. Run the command from the map folder, or pass `--map <folder>`.
- **A language file is ignored.** Only `en`, `es`, `fr`, `de`, `pt`, `ru`, `zh`, `ja`, `ar`, `hi` and `ko` are loaded; `verify` warns about any other file name.
