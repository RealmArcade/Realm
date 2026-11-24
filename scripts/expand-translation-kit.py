#!/usr/bin/env python3
"""Expands an index-keyed translation file into a kit file the merge script accepts.

A translator writes {"1": "...", "2": "..."} in the order of source.json. This
maps each index back to its English key and refuses the file unless every index
is present exactly once, every placeholder survives, every line break survives,
and each technical token the English carries appears in the translation.
"""

import json
import pathlib
import re
import sys

SOURCE = pathlib.Path("docs/translation-kit/source.json")
KIT_DIRECTORY = pathlib.Path("docs/translation-kit")
PLACEHOLDER = re.compile(r"\{(\d)\}")
TECHNICAL_TOKENS = ["Steam", ".rmap", "terrain.json", "SteamID64", "Discord", "Workshop", "discord.gg", "authorship_key"]


def main():
    indexed_path = pathlib.Path(sys.argv[1])
    language = sys.argv[2]
    source = json.loads(SOURCE.read_text(encoding="utf-8"))
    keys = list(source)
    indexed = json.loads(indexed_path.read_text(encoding="utf-8-sig"))

    problems = []
    expected_indices = [str(number) for number in range(1, len(keys) + 1)]
    missing = [index for index in expected_indices if index not in indexed]
    extra = [index for index in indexed if index not in expected_indices]
    if missing:
        problems.append(f"missing indices: {', '.join(missing)}")
    if extra:
        problems.append(f"unexpected indices: {', '.join(extra)}")

    kit = {}
    for index, key in enumerate(keys, start=1):
        value = indexed.get(str(index))
        if value is None:
            continue
        if not value.strip():
            problems.append(f"#{index} is empty: {key[:60]}")
            continue
        if sorted(PLACEHOLDER.findall(key)) != sorted(PLACEHOLDER.findall(value)):
            problems.append(f"#{index} placeholder mismatch: {key[:60]} -> {value[:60]}")
        if key.count("\n") != value.count("\n"):
            problems.append(f"#{index} line-break mismatch: {key[:60]}")
        for token in TECHNICAL_TOKENS:
            if token in key and token not in value:
                problems.append(f"#{index} dropped '{token}': {value[:60]}")
        kit[key] = value

    if problems:
        for problem in problems:
            print(problem)
        sys.exit(f"{language}: {len(problems)} problem(s), nothing written")

    destination = KIT_DIRECTORY / f"{language}.json"
    destination.write_text(json.dumps(kit, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{language}: wrote {len(kit)} of {len(keys)} entries to {destination}")


main()
