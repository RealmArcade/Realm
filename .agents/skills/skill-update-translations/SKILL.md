---
name: skill-update-translations
description: Automatically extracts, synchronizes, translates, and validates game localization catalogs (en.json, es.json, and other locales) for the Realm client using Roslyn analysis and AI-assisted batch translation.
---

# Skill: Update Translations (skill-update-translations)

## Trigger Conditions
Activates whenever:
- Adding, updating, or fixing UI localization strings across `Realm.Client/locale/*.json`.
- Synchronizing missing translation keys between `en.json` and target language catalogs (`es`, `fr`, `de`, `pt`, `ru`, `zh`, `ja`, `ar`, `hi`).
- Auditing UI screens for unlocalized or overflowed text in the Godot client.

---

## Core Invariants & Architectural Rules

1. **Zero Direct Manual Edits**:
   - Never manually insert or invent translation keys directly into `en.json` or target locale JSON files (`es.json`, etc.).
   - All English keys MUST originate from source code analysis or runtime registration.
   - Target language translations MUST be generated automatically via `Realm.TranslationTool` or via Antigravity batch translation.

2. **Dual-Path English Extraction (`en.json`)**:
   - **Static Extraction**: Run `Realm.LocalizationGenerator` using Roslyn and regex to scan `.cs`, `.js`, and `.tscn` files:
     ```powershell
     dotnet run --project Realm.LocalizationGenerator\Realm.LocalizationGenerator.csproj Realm.slnx Realm.Client\locale\en.json
     ```
   - **Dynamic Runtime Registration**: In Debug builds (`OS.IsDebugBuild()`), `LocalizationManager.TranslateKeyDebug` intercepts any unresolved `Translate(...)` calls in real-time and flushes new keys directly to `en.json`.

3. **Translations Generation (`<locale>.json`)**:
   - **Method A (Local Gemma / Ollama)**:
     ```powershell
     dotnet run --project Realm.TranslationTool\Realm.TranslationTool.csproj
     ```
   - **Method B (Antigravity Batch Translation)**:
     Extract diffs between `en.json` and target locale, apply the Master Translation Prompt contract, and merge valid JSON back into `Realm.Client/locale/<locale>.json`.

4. **Scope Exclusions**:
   - Do NOT localize map-specific assets for the `chaos` map.
   - Spell title localizations are reserved for a separate map-level JSON file in a future task.

---

## Master Translation Prompt Contract

When delegating translation or batch correction to an LLM / Antigravity, strictly enforce the following prompt contract:

```markdown
You are a professional video game localization specialist translating for a real-time strategy (RTS) PC game ("Realm").
Translate the following JSON dictionary of English strings into {{TargetLanguage}} (locale: {{LocaleCode}}).

### Mandatory Rules:
1. **Identical Keys**: The output JSON keys MUST match the input English keys character-for-character. Never translate or alter keys.
2. **Translate Values Only**: Provide natural, idiomatic gaming terminology suitable for RTS UI, settings menus, and editor tools.
3. **Parameter Preservation**: Keep all format specifiers and placeholders exactly intact: `{0}`, `{1}`, `{timeName}`, `{0:+0.0;-0.0;0.0}`, `{1:F1}`, etc.
4. **BBCode Preservation**: Never modify or strip BBCode tags: `[b]`, `[/b]`, `[i]`, `[color=...]`, `[url]`, `[p]`, `[h2]`, `[list]`, `[*]`, etc.
5. **Casing Matching**:
   - If the English source is UPPERCASE (e.g., "HOST A GAME"), output UPPERCASE (e.g., "CREAR PARTIDA").
   - If Title Case or Sentence case, use standard natural capitalization.
6. **Hotkeys and Symbols**: Preserve shortcut hints such as `(F9)`, `(M)`, `(7)`, `[rigged]`, etc.
7. **RTS Terminology**:
   - "Lobby" -> "Sala"
   - "Host a game" -> "Crear partida"
   - "Match" -> "Partida"
   - "Fog of War" -> "Niebla de guerra"
   - "Asset Browser" -> "Explorador de recursos"
   - "Terrain" -> "Terreno"
   - "Socket" -> "Punto de anclaje"
   - "Attack-Move" -> "Atacar-Mover"
8. **Output**: Return ONLY valid, formatted JSON with identical structure.
```

---

## Verification & Screen Testing Protocol

1. **In-Game Switching**:
   - Launch Godot and switch language in **Settings -> Game -> Language**.
   - Verify layout direction for Arabic (`ar`): Ensure `UIManager.Instance.LayoutDirection = Control.LayoutDirectionEnum.Rtl`.

2. **Screen-by-Screen Coverage**:
   - **Main Menu & Settings**: Audio, Video, Gameplay, Controls, Replays.
   - **Custom Lobby Browser**: Server list, player slots, host dialogs, NAT classification.
   - **Map Editor**: Top bar, tool palettes, terrain brushes, inspector dialogs, asset manager.
   - **Live Game HUD**: Unit selection panel, command cards, resource counters, pause overlay, victory/defeat dialogs.

3. **Visual Quality & Godot Constraints**:
   - Check for text clipping or container overflow. Spanish and German strings are typically 20-30% longer than English.
   - Ensure containers with `autowrap_mode` define `custom_minimum_size.x` and `size_flags_horizontal = SIZE_EXPAND_FILL`.
   - Ensure no hardcoded Unicode escapes (`\uXXXX`) exist in scripts; reference `UnicodeIcons` constants instead.

4. **Google Lens Mobile Verification**:
   - For non-Latin or complex alphabets (`ru`, `zh`, `ja`, `ar`, `hi`), point a mobile camera running Google Lens at the screen.
   - Confirm that the reverse translation conveys the expected gameplay function and has no corrupted or awkward phrasing.
