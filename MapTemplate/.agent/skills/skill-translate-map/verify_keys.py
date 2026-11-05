#!/usr/bin/env python3
"""Read, verify and prune the locale catalogs of a Realm map.

The translatable text lives in metadata.json. The client looks it up in
locale/<code>.json when the map loads, so the *English text itself* is the key.

Commands:
  list [--locale CODE]      Print the strings to translate as a JSON batch.
  verify                    Check every locale/*.json against metadata.json.
  prune                     Remove entries whose key no longer exists in metadata.json.
  merge CODE FILE           Merge a JSON batch of translations into locale/CODE.json.

Options:
  --map PATH                Map folder to work on. Defaults to the current directory.

Examples:
  python verify_keys.py list --locale es > batch.es.json
  python verify_keys.py merge es batch.es.json
  python verify_keys.py verify
  python verify_keys.py prune
"""

import json
import re
import sys
from pathlib import Path

# The languages the Realm client ships catalogs for; a file named for any other
# code is never loaded, so its translations would silently do nothing.
SUPPORTED_LOCALES = ("en", "es", "fr", "de", "pt", "ru", "zh", "ja", "ar", "hi", "ko")

# Template collections whose Name/Description the client renders in-game. Weapons,
# props and resources are editor-facing, so translating them only creates keys
# nothing reads.
TEMPLATE_COLLECTIONS = ("Units", "Buildings", "Abilities", "Items", "Upgrades")

BBCODE_TAGS = {
    "b", "i", "u", "s", "strike", "url", "img", "list", "olist", "hr", "p",
    "h1", "h2", "h3", "noparse", "code", "quote", "color", "table", "spoiler",
    "center", "right", "left", "size", "font", "sub", "sup", "*",
}

# {{Ability.Effect0.Amount}} in a tooltip, {0} in text a script formats itself.
TOKEN_RE = re.compile(r"\{\{[^{}]*\}\}|\{[0-9]+\}")
BRACKET_RE = re.compile(r"\[[^\[\]]*\]")
TAG_STRIP_RE = re.compile(r"\[[^\[\]]*\]")
ROLE_RE = re.compile(r"^\[([^\[\]=/]{1,24})\] ")

MAXIMUM_REPORTED_ENTRIES = 20


def fail(message):
    print(f"error: {message}", file=sys.stderr)
    return 2


def parse_arguments(argv):
    map_path = Path.cwd()
    locale = None
    positional = []

    index = 0
    while index < len(argv):
        argument = argv[index]
        if argument in ("--map", "--locale"):
            index += 1
            if index >= len(argv):
                raise ValueError(f"{argument} needs a value")
            if argument == "--map":
                map_path = Path(argv[index])
            else:
                locale = argv[index]
        elif argument.startswith("--map="):
            map_path = Path(argument.split("=", 1)[1])
        elif argument.startswith("--locale="):
            locale = argument.split("=", 1)[1]
        else:
            positional.append(argument)
        index += 1

    return map_path, locale, positional


def load_metadata(map_path):
    metadata_path = map_path / "metadata.json"
    if not metadata_path.is_file():
        raise FileNotFoundError(f"no metadata.json in {map_path}")
    return json.loads(metadata_path.read_text(encoding="utf-8"))


def visible_text(text):
    """The text a player sees, with BBCode tags removed."""
    return TAG_STRIP_RE.sub("", text).strip()


def collect_sources(metadata):
    """Return (required, optional, alternatives, roles).

    required     keys the map should translate: template names and descriptions.
    optional     keys that are rendered but may be left in English: an ability's
                 Role, which the client prints as "[Damage]" in front of the
                 description, and the parts of a description that already carries
                 its own "[Damage] " prefix.
    alternatives required key -> (role key, body key) that may stand in for it.
    """
    required = {}
    optional = set()
    alternatives = {}
    roles = set()

    templates = metadata.get("Templates") or {}
    for collection in TEMPLATE_COLLECTIONS:
        entries = templates.get(collection) or []
        if isinstance(entries, dict):
            entries = list(entries.values())

        for entry in entries:
            if not isinstance(entry, dict):
                continue

            name = entry.get("Name")
            if isinstance(name, str):
                # A name is keyed without its BBCode, and the markup stays in
                # metadata.json, so the reference text carries no tags either.
                key = visible_text(name)
                if key:
                    required.setdefault(key, key)

            description = entry.get("Description")
            if not isinstance(description, str) or not visible_text(description):
                continue

            required.setdefault(description, description)

            # The client prints an ability's Role in front of its description and
            # looks it up on its own, so "Damage" is a key of its own.
            role = entry.get("Role")
            if isinstance(role, str) and role.strip():
                roles.add(role.strip())
                optional.add(role.strip())

            # A description may instead carry that prefix itself, "[Damage] Deals ...".
            # The client then looks up the role and the sentence separately, so a
            # translator may key those instead of the whole rendered line.
            match = ROLE_RE.match(description)
            if not match:
                continue

            inline_role = match.group(1)
            if inline_role.lower() in BBCODE_TAGS:
                continue

            body = description[match.end():]
            optional.add(inline_role)
            optional.add(body)
            alternatives[description] = (inline_role, body)

    return required, optional, alternatives, roles


def token_signature(text):
    return " ".join(sorted(TOKEN_RE.findall(text)))


def normalize_tag(token):
    inner = token[1:-1].strip()
    closing = inner.startswith("/")
    if closing:
        inner = inner[1:].strip()
    inner = inner.split("=", 1)[0].strip().lower()
    if inner not in BBCODE_TAGS:
        return None
    return "/" + inner if closing else inner


def bracket_signatures(text):
    """(BBCode tag signature, other bracket hints such as [Damage] or [Q])."""
    tags = []
    hints = []
    for token in BRACKET_RE.findall(text):
        tag = normalize_tag(token)
        if tag is None:
            hints.append(token)
        else:
            tags.append(tag)
    tags.sort()
    hints.sort()
    return " ".join(tags), " ".join(hints)


def load_catalog(path):
    try:
        catalog = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ValueError(f"{path}: {error}") from error
    if not isinstance(catalog, dict):
        raise ValueError(f'{path}: expected a JSON object of {{"English": "translated"}}')
    return catalog


def write_catalog(path, catalog):
    ordered = {key: catalog[key] for key in sorted(catalog, key=str.lower)}
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(ordered, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def catalog_paths(map_path):
    locale_directory = map_path / "locale"
    if not locale_directory.is_dir():
        return []
    return sorted(locale_directory.glob("*.json"))


def translated(catalog, key):
    value = catalog.get(key)
    return isinstance(value, str) and bool(value.strip())


def unmet_keys(catalog, required, alternatives):
    missing = []
    for key in required:
        if translated(catalog, key):
            continue
        alternative = alternatives.get(key)
        if alternative and translated(catalog, alternative[0]) and translated(catalog, alternative[1]):
            continue
        missing.append(key)
    return missing


def report(title, entries, is_error):
    if not entries:
        print(f"  {title}: 0")
        return 0

    print(f"  {title}: {len(entries)}")
    for entry in entries[:MAXIMUM_REPORTED_ENTRIES]:
        print(f"    {entry}")
    if len(entries) > MAXIMUM_REPORTED_ENTRIES:
        print(f"    ... and {len(entries) - MAXIMUM_REPORTED_ENTRIES} more")
    return len(entries) if is_error else 0


def command_list(map_path, locale):
    required, _, alternatives, roles = collect_sources(load_metadata(map_path))

    catalog = {}
    if locale:
        path = map_path / "locale" / f"{locale.lower()}.json"
        catalog = load_catalog(path) if path.is_file() else {}
        required = {key: required[key] for key in unmet_keys(catalog, required, alternatives)}

    # Values repeat the key so the file is a ready-to-fill batch: replace each
    # value with the translation and hand it back to `merge`. Role words are part of
    # the batch because the client renders them in front of the tooltip.
    batch = {key: key for key in required}
    for role in sorted(roles):
        if translated(catalog, role):
            continue
        batch[role] = role

    print(json.dumps(batch, ensure_ascii=False, indent=2))
    return 0


def command_verify(map_path):
    required, optional, alternatives, _ = collect_sources(load_metadata(map_path))
    paths = catalog_paths(map_path)

    print(f"map: {map_path}")
    print(f"metadata.json: {len(required)} keys required, {len(optional)} optional parts")
    if not paths:
        print()
        print("no locale/*.json yet: every player reads the English text.")
        print("Write one file per language, named for the locale code (es.json, fr.json, ...).")
        return 0

    issues = 0
    for path in paths:
        code = path.stem.lower()
        print()
        print(f"--- {code} ({path.name}) ---")

        if code not in SUPPORTED_LOCALES:
            print(f"  warning: '{code}' is not a language the client loads ({', '.join(SUPPORTED_LOCALES)})")

        try:
            catalog = load_catalog(path)
        except ValueError as error:
            issues += 1
            print(f"  {error}")
            continue

        missing = unmet_keys(catalog, required, alternatives)
        empty = [key for key, value in catalog.items() if not isinstance(value, str) or not value.strip()]

        token_mismatches = []
        tag_mismatches = []
        hint_mismatches = []
        untranslated_values = []

        for key, source in required.items():
            value = catalog.get(key)
            if not isinstance(value, str) or not value.strip():
                continue

            expected_tokens = token_signature(source)
            actual_tokens = token_signature(value)
            if expected_tokens != actual_tokens:
                token_mismatches.append(f'"{key}" expected [{expected_tokens}] but found [{actual_tokens}]')

            expected_tags, expected_hints = bracket_signatures(source)
            actual_tags, actual_hints = bracket_signatures(value)
            if expected_tags != actual_tags:
                tag_mismatches.append(f'"{key}" expected [{expected_tags}] but found [{actual_tags}]')
            if expected_hints != actual_hints:
                hint_mismatches.append(f'"{key}" expected [{expected_hints}] but found [{actual_hints}]')

            if code != "en" and value == source:
                untranslated_values.append(key)

        orphan_keys = [key for key in catalog if key not in required and key not in optional]

        issues += report("Missing keys", missing, True)
        issues += report("Empty values", empty, True)
        issues += report("Placeholder mismatches", token_mismatches, True)
        issues += report("BBCode tag mismatches", tag_mismatches, True)
        issues += report("Bracket hint mismatches (role tags, [Q] hotkeys)", hint_mismatches, True)
        issues += report("Orphan keys (no longer in metadata.json)", orphan_keys, True)
        report("Values identical to English", untranslated_values, False)

    print()
    if issues:
        print(f"verification failed with {issues} issue(s).")
        return 1

    print("all locale catalogs are valid.")
    return 0


def command_prune(map_path):
    required, optional, _, _ = collect_sources(load_metadata(map_path))
    known = set(required) | optional

    removed_total = 0
    for path in catalog_paths(map_path):
        catalog = load_catalog(path)
        orphans = [key for key in catalog if key not in known]

        if not orphans:
            print(f"{path.stem}: nothing to prune")
            continue

        if len(orphans) * 2 > len(catalog):
            return fail(
                f"pruning {path.stem} would remove {len(orphans)} of {len(catalog)} keys; "
                "check that metadata.json still holds the text before running this again"
            )

        for key in orphans:
            print(f'  {path.stem}: removed "{key}"')
            del catalog[key]

        write_catalog(path, catalog)
        removed_total += len(orphans)
        print(f"{path.stem}: pruned {len(orphans)} key(s)")

    print()
    print(f"pruned {removed_total} orphan key(s).")
    return 0


def command_merge(map_path, positional):
    if len(positional) != 2:
        return fail("merge needs a locale code and a JSON file: merge <code> <file>")

    locale = positional[0].lower()
    batch_path = Path(positional[1])
    if not batch_path.is_file():
        return fail(f"no batch file at {batch_path}")

    required, optional, _, _ = collect_sources(load_metadata(map_path))
    known = set(required) | optional

    try:
        batch = load_catalog(batch_path)
    except ValueError as error:
        return fail(str(error))

    path = map_path / "locale" / f"{locale}.json"
    catalog = load_catalog(path) if path.is_file() else {}

    unknown = [key for key in batch if key not in known]
    if unknown:
        print(f"warning: {len(unknown)} key(s) are not in metadata.json and will read as unused:", file=sys.stderr)
        for key in unknown[:MAXIMUM_REPORTED_ENTRIES]:
            print(f'  {key}', file=sys.stderr)

    for key, value in batch.items():
        if not isinstance(value, str) or not value.strip():
            return fail(f'"{key}" has no translation')
        catalog[key] = value

    write_catalog(path, catalog)
    print(f"{locale}: wrote {len(catalog)} key(s) to {path}")
    return 0


def main(argv):
    try:
        map_path, locale, positional = parse_arguments(argv)
    except ValueError as error:
        return fail(str(error))

    if not map_path.is_dir():
        return fail(f"no map folder at {map_path}")

    command = positional[0].lower() if positional else "verify"
    arguments = positional[1:]

    try:
        if command == "list":
            return command_list(map_path, locale)
        if command == "verify":
            return command_verify(map_path)
        if command == "prune":
            return command_prune(map_path)
        if command == "merge":
            return command_merge(map_path, arguments)
    except (OSError, ValueError) as error:
        return fail(str(error))

    return fail(f"unknown command '{command}' (expected list, verify, prune or merge)")


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
