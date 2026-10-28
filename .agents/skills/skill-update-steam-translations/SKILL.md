---
name: skill-update-steam-translations
description: Translates and validates the Realm Steam store page (short description, About This Game, Early Access text, system requirements) across every store language, working from a transient Steamworks JSON export that is never committed to the repository.
---

# Skill: Update Steam Store Translations (skill-update-steam-translations)

## Trigger Conditions
Activates whenever:
- The English text of the Steam store page changes (short description, About This Game, Early Access blurb, system requirements).
- A store language is added, re-enabled, or reviewed on the Steamworks partner site.
- Steam store text must be synchronized with changed in-game terminology (see `skill-update-translations`).

This skill covers **store page text only**. In-game `Realm.Client/locale/*.json` catalogs belong to `skill-update-translations` and must not be touched here.

---

## Data Ownership (Never Commit Store Page Files)

The store page JSON is a **transient working file**:
- English is authored directly on the Steamworks partner site. The Steamworks store page is the single source of truth for store text.
- The file is downloaded, translated, re-uploaded, and then deleted.
- **Never** commit it, never add it to `MapTemplate/`, `Realm.Client/`, or any tracked folder, and never add a `.gitignore` entry that implies it lives in the repository.
- Keep the working copy outside the repository entirely (for example `%TEMP%\realm-storepage\`) so `git status` stays clean. The filename is supplied by Steamworks and normally looks like `storepage_<appid>_all.json`.
- If a working copy was accidentally created inside the repository, delete it before finishing rather than committing or ignoring it.

The repository therefore never contains store page source text, translations, or caches for it.

---

## Workflow

### 1. Author or Review English on Steamworks
Edit the English store page in the Steamworks partner site until the copy is final. Do not begin translation while English is still changing, because every change invalidates the later languages.

### 2. Export the Store Page JSON
Download the store page JSON from Steamworks into the scratch directory outside the repository.

Confirm the shape before translating:
- `itemid` identifies the app and must be preserved verbatim.
- `languages` is a map of Steam language keys to field maps.
- `languages.english` holds the authoritative source text.

### 3. Translate Locally With the Agent
Read `languages.english` and produce a translation for every other language key present in the file. Use the Master Store Page Prompt Contract below.

Constraints:
- Translate **only** the language keys already present in the export. Never invent keys for languages the store does not offer (the store page has no Arabic or Hindi tab even though the game ships those locales).
- Never rename, add, remove, or reorder the **field keys** inside a language object.
- Preserve `itemid` and every language key exactly.

### 4. Validate Before Re-upload
Run the parity check supplied at the end of this skill against the edited file. It compares BBCode tokens between English and each translated language and reports every mismatch. Re-upload nothing until it reports zero mismatches.

Also spot-check by hand:
- No leading or trailing quotation marks, no markdown code fences, no `\n` escape sequences that should be real newlines, no untranslated English left in a translated field.
- Steam store BBCode such as `[p]`, `[/p]`, `[b]`, `[/b]`, `[h2]`, `[/h2]`, `[list]`, `[/list]`, `[*]`, `[b]` inside lists, `[url=...]`, `[i]`, `[u]` is byte-identical to English, including closing tags.
- Numbers, version strings, byte counts, OS names (`Windows`, `macOS`, `Linux`), hardware names, and API names in system requirements are unchanged.
- Game names and proper nouns match the in-game locale catalogs, not a fresh literal translation.

### 5. Re-upload to Steamworks
Paste each language's field values into the matching language tab on the Steamworks store page editor, then save. Review the rendered preview for at least the longest and the right-to-left languages before publishing.

### 6. Delete the Scratch File
Remove the working JSON (and any intermediate translation exports) once Steamworks shows the saved text. Nothing from this workflow persists in the repository.

---

## Master Store Page Prompt Contract

```markdown
You are a professional video game localization specialist translating the Steam store page of "Realm", an open-source tactical real-time strategy (RTS) arcade game.

Input is a JSON object where keys are Steam field names and values are English store page text. Translate the values into {{TargetLanguage}} (Steam language key: {{SteamLanguageKey}}).

### Mandatory Rules:
1. **Identical Field Keys**: Output the same field keys character-for-character. Never translate, rename, add, or drop a key.
2. **Translate Values Only**: Produce natural, idiomatic marketing and technical copy suitable for a Steam store page. Keep the persuasive tone of headlines and the plain tone of system requirements.
3. **BBCode Preservation**: Reproduce every BBCode tag exactly as written, in the same position relative to the text: `[p]`, `[/p]`, `[b]`, `[/b]`, `[i]`, `[/i]`, `[u]`, `[/u]`, `[h2]`, `[/h2]`, `[list]`, `[/list]`, `[*]`, `[url=...]`, `[/url]`. Never add, remove, reorder, or translate tags. Never emit markdown instead.
4. **Untouched Technical Data**: Preserve OS names, hardware names, versions, numbers, units, and API names in system requirements exactly as written.
5. **Output**: Return ONLY valid JSON with the same structure. No commentary, no code fences, no leading or trailing quotation marks.
```

---

## Store Page JSON Shape

```json
{
  "itemid": "<appid>",
  "languages": {
    "english": { "<fieldKey>": "<English text>" },
    "<steamLanguageKey>": { "<fieldKey>": "<translated text>" }
  }
}
```

Field keys are Steamworks-defined and are whatever the export contains (typically a short description, an About This Game block, an Early Access block, and system requirement blocks). Treat them as opaque identifiers.

## Steam Language Keys

Only handle keys that actually appear in the export. Steam store language keys are drawn from this set:

| Key | Language | Key | Language |
| --- | --- | --- | --- |
| `english` | English (source) | `japanese` | Japanese |
| `french` | French | `koreana` | Korean |
| `italian` | Italian | `schinese` | Simplified Chinese |
| `german` | German | `tchinese` | Traditional Chinese |
| `spanish` | Spanish (Spain) | `portuguese` | Portuguese (Portugal) |
| `latam` | Spanish (Latin America) | `brazilian` | Portuguese (Brazil) |
| `russian` | Russian | `polish` | Polish |
| `dutch` | Dutch | `czech` | Czech |
| `danish` | Danish | `hungarian` | Hungarian |
| `finnish` | Finnish | `romanian` | Romanian |
| `norwegian` | Norwegian | `bulgarian` | Bulgarian |
| `swedish` | Swedish | `greek` | Greek |
| `turkish` | Turkish | `ukrainian` | Ukrainian |
| `thai` | Thai | `vietnamese` | Vietnamese |
| `indonesian` | Indonesian | `malay` | Malay |

Realm ships in-game locales `es`, `fr`, `de`, `pt`, `ru`, `zh`, `ja`, `ar`, `hi`. The store page has no Arabic or Hindi tab, so those two locales have no store counterpart; `zh` maps to whichever Chinese variant the store actually enables.

---

## Validation Script

Run this against the edited store page JSON before re-uploading. It reports every BBCode mismatch per language and per field, and exits non-zero when anything differs.

```python
import json, re, sys

path = sys.argv[1]
with open(path, encoding="utf-8") as handle:
    data = json.load(handle)

english_fields = data["languages"]["english"]
token = re.compile(r"\[[^\[\]]*\]")
mismatches = 0
translated_fields = 0

for language_key, fields in data["languages"].items():
    if language_key == "english":
        continue
    for field_key, english_text in english_fields.items():
        localized_text = fields.get(field_key, "")
        translated_fields += 1
        expected = sorted(token.findall(english_text))
        actual = sorted(token.findall(localized_text))
        if expected != actual:
            mismatches += 1
            print(f"{language_key}/{field_key}: expected {expected} found {actual}")
        if not localized_text.strip():
            mismatches += 1
            print(f"{language_key}/{field_key}: empty translation")

print(f"checked {translated_fields} translated field(s), {mismatches} mismatch(es)")
sys.exit(1 if mismatches else 0)
```

Usage: `python check_storepage.py "<scratch>/storepage_<appid>_all.json"`
