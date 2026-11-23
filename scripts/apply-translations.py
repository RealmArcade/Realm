#!/usr/bin/env python3
"""Merges a filled translation kit into a locale catalog.

The kit is a JSON object of English key to translated text. This script checks
every key against the current English catalog, refuses keys that no longer
exist, skips entries left empty, and appends what is left to the catalog
without reordering or reformatting the file. Run it from the repository root:

    python3 scripts/apply-translations.py docs/translation-kit/de.json de
    python3 scripts/apply-translations.py docs/translation-kit/de.json de --dry-run

Nothing is written until every key has been checked.
"""

import argparse
import json
import pathlib
import sys

DEFAULT_LOCALE_DIRECTORY = pathlib.Path("Realm.Client/locale")
ENGLISH_CATALOG = "en.json"


def read_catalog(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def append_entries(catalog_text, entries):
    """Adds entries before the closing brace, leaving every existing line as it was."""
    closing_brace = catalog_text.rstrip().rfind("}")
    head = catalog_text[:closing_brace].rstrip("\n")
    addition = "".join(
        ",\n  " + json.dumps(key, ensure_ascii=False) + ": " + json.dumps(value, ensure_ascii=False)
        for key, value in entries
    )
    return head + addition + "\n}\n"


def main():
    parser = argparse.ArgumentParser(description="Merge a filled translation kit into a locale catalog.")
    parser.add_argument("kit", type=pathlib.Path, help="the filled kit file, a JSON object of English key to text")
    parser.add_argument("language", help="the catalog to write, such as de or ja")
    parser.add_argument("--locale-dir", type=pathlib.Path, default=DEFAULT_LOCALE_DIRECTORY,
                        help=f"where the catalogs live (default: {DEFAULT_LOCALE_DIRECTORY})")
    parser.add_argument("--dry-run", action="store_true", help="report what would be written and write nothing")
    arguments = parser.parse_args()

    english_path = arguments.locale_dir / ENGLISH_CATALOG
    catalog_path = arguments.locale_dir / f"{arguments.language}.json"
    if not english_path.is_file():
        sys.exit(f"The English catalog is not at {english_path}; run this from the repository root.")
    if not catalog_path.is_file():
        sys.exit(f"There is no catalog for '{arguments.language}' at {catalog_path}.")

    english = read_catalog(english_path)
    catalog_text = catalog_path.read_text(encoding="utf-8")
    catalog = json.loads(catalog_text)
    kit = read_catalog(arguments.kit)
    if not isinstance(kit, dict):
        sys.exit(f"{arguments.kit} is not a JSON object of English key to translated text.")

    unknown = sorted(key for key in kit if key not in english)
    if unknown:
        sys.exit(f"{len(unknown)} key(s) in {arguments.kit} are not in {english_path}: {unknown[:5]}")

    already_present = sorted(key for key in kit if key in catalog)
    filled = [(key, kit[key]) for key in kit if kit[key].strip() and key not in catalog]
    empty = sorted(key for key in kit if not kit[key].strip())

    print(f"{arguments.language}: {len(filled)} to add, {len(empty)} still empty, "
          f"{len(already_present)} already translated")
    if empty:
        print("  still empty: " + ", ".join(repr(key) for key in empty[:5]))
    if already_present:
        print("  already present: " + ", ".join(repr(key) for key in already_present[:5]))
    if not filled:
        print("Nothing to write.")
        return

    merged = append_entries(catalog_text, filled)
    try:
        json.loads(merged)
    except json.JSONDecodeError as error:
        sys.exit(f"Refusing to write {catalog_path}: the merged catalog would not parse ({error}).")

    if arguments.dry_run:
        print(f"Dry run: {catalog_path} would gain {len(filled)} entr(ies).")
        return

    catalog_path.write_text(merged, encoding="utf-8")
    print(f"Added {len(filled)} entr(ies) to {catalog_path}. Run the translation tool's prune and verify next.")


if __name__ == "__main__":
    main()
