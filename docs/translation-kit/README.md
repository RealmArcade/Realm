# Translation kit for the Steam surfaces

The Steam-first migration replaced the game's lobby, identity, map-distribution and publishing surfaces, and in doing so
added the strings the lobby, map-library, publishing and diagnostics surfaces now show. This folder holds those strings and one filled file per language; a translator works in one
file, and nothing in the game reads these files. All ten languages are filled, and `Realm.TranslationTool -- verify`
reports nothing missing in any of them, so what is left here is review and the next string that gets added.

Two of the strings the migration added were carried over automatically, because their meaning had not changed
(`✂️ Copy` and `Matches played`). Everything else here is genuinely new text, and two things about it are worth knowing
before starting:

- **This is a new vocabulary, not a rewording.** "Lobby", "map", "host", "publish" and "trust mark" already have words in
  every catalog. Match them; `glossary.md` shows the sentences that already use each one, per language.
- **Do not carry an old sentence over because it looks similar.** The screens changed meaning as well as wording. The
  private-lobby message no longer mentions an address, because a lobby has none; a sign-in message became its opposite
  ("Continue without Steam"). A translation that reads well but says the retired thing is worse than an English string.

## Files

| File | What it holds |
| --- | --- |
| `source.json` | Every English string these surfaces show, key to text. Read this, never edit it. |
| `<language>.json` | One per catalog (`ar`, `de`, `es`, `fr`, `hi`, `ja`, `ko`, `pt`, `ru`, `zh`): the same keys, each with its translated value. A new English string is added to all ten as an empty value and filled the same way. |
| `glossary.md` | Per language, the existing translated sentences that use "lobby", "map", "host", "Workshop", "Steam", "invite", "publish", "code", "join", "trust", "version" and "seat". Generated from the catalogs, not written by hand. |

## Filling a language

Translate only the values. Keys are the game's lookup strings and changing one silently loses the translation. A
language written before this pass can be checked against the vocabulary the others settled on: German, French, Spanish,
Portuguese, Russian, Chinese, Japanese, Korean, Arabic and Hindi each use the word their catalog already used for lobby,
map, host, publish and invite, so a new sentence should sound like the ones beside it.

- **Placeholders stay exactly as they are**, including their number: `{0}`, `{1}`, `{2}`. Word order may move them, as
  long as `{0}` still means what it means in English.
- **Line breaks** inside a value are written as the two characters `\n` (the English source shows them where the game
  breaks a line).
- **Emoji and icons stay** (`✂️`, `⚠️`); they are not words.
- **Caps stay caps** in the label strings, because they are drawn as titles and buttons.
- **"Steam" is a brand**, so it is not translated in any of the nine catalogs; "Workshop" refers to Steam Workshop.
- **Leave a value empty** if you are unsure. An empty value is skipped, and that string keeps showing its English until
  somebody fills it; a guessed translation shows instead.

## Applying a filled file

From the repository root:

```
python3 scripts/apply-translations.py docs/translation-kit/de.json de --dry-run
python3 scripts/apply-translations.py docs/translation-kit/de.json de
```

The script refuses a key that is no longer in `Realm.Client/locale/en.json`, skips values left empty, appends what is
left to the catalog without reordering or reformatting it, and writes nothing unless the merged catalog parses. Then
check the result with the translation tool:

```
dotnet run --project Realm.TranslationTool -- prune
dotnet run --project Realm.TranslationTool -- verify
```

`prune` must report nothing to remove, and `verify` should report the remaining missing count for that language, which is
the number of values still empty.

## Checking it on screen

The strings are drawn by the lobby browser, the lobby room, the invite and profile dialogs, the map library and details
screens, the publish dialog, the diagnostics export, and the menu's Steam state line. The test plan's local group is the
cheapest pass, run once in the language being checked: a string that appears in English either has no translation yet, or
is drawn through a path that does not register its key, which is a bug rather than missing translation work.
