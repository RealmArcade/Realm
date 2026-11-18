# Workshop archive and mirror tool

A developer tool that keeps a verified local copy of every Workshop item Realm publishes, so a creator deleting or
privatising a map cannot erase it, and stages a reviewed copy for a Realm-owned mirror. It is not part of the game, is
not in `Realm.slnx`, and never runs inside a match or against a player's Steam account.

## Survey, and what was chosen

`pairofcrocs/steam-workshop-archiver` was evaluated first. It is a self-hosted web application that runs whole-Workshop
jobs, skips unchanged items, lists through `IPublishedFileService/QueryFiles` and downloads with SteamCMD. It is a good
fit for a plain mirror of an app's Workshop, and it was not adopted here, for three reasons this repository needs and
that tool does not cover:

- the archive has to carry a manifest the game's package reader can verify, per item and per file;
- the attribution a mirror reviewer needs comes from the archived map's own `references.json`, because a composed item
  republishes another creator's assets and scripts under its uploader's name;
- mirror staging is a reviewed, one-item-at-a-time workflow with a recorded reviewer and note, not a scheduled job.

Adopting it would have meant running and maintaining a web service beside it for those three, so the smallest path is a
single local command line tool over SteamCMD, which is Valve's supported download path. `WorkshopDL` and
`SteamWorkshopDownloader` are third-party downloaders and were not used for the canonical archive. `DepotDownloader`
does not handle Workshop items. The Steam Web API's `IPublishedFileService/QueryFiles` is the listing and
`ISteamUGC` is deliberately not used: it is tied to a running client and to that account's subscriptions.

## Setup

Environment, never committed:

| Variable | What it is |
| --- | --- |
| `STEAM_WEB_API_KEY` | The Steam Web API key the listing uses. Create one under Steam's Web API key page. |
| `STEAM_ARCHIVE_ACCOUNT` | The dedicated archive account SteamCMD logs in as. It must own the app, and it must not be the developer's personal or partner account. Steam Guard has to be completed interactively once on the machine that runs the tool. |

SteamCMD must be on `PATH`. The archive root must be outside the repository, for example an external drive: it holds
large binaries and other creators' content, and committing it would put both into this project's history.

## Commands

```pwsh
# What would be considered, without downloading anything
dotnet run --project tools/WorkshopArchive -- list --archive-root D:\RealmArchive --appid 5372260,5537420

# Archive what changed, download what is new, write manifests, attribution and reports
dotnet run --project tools/WorkshopArchive -- archive --archive-root D:\RealmArchive --appid 5372260,5537420

# One item only, or a full re-verification and re-download
dotnet run --project tools/WorkshopArchive -- archive --archive-root D:\RealmArchive --item 1234567890
dotnet run --project tools/WorkshopArchive -- archive --archive-root D:\RealmArchive --full

# Recompute every manifest and report files whose bytes no longer match
dotnet run --project tools/WorkshopArchive -- verify --archive-root D:\RealmArchive --appid 5372260
```

A run is incremental: an item is fetched only when its `hcontent_file` or `time_updated` differs from the archived
record, so a second run downloads nothing unchanged. An item Steam no longer returns stays in the archive and is marked
`missing`; nothing is deleted because the Workshop stopped listing it.

Layout under the archive root:

```
<root>/<appid>/<publishedfileid>/metadata.json     the item as Steam stated it, raw
<root>/<appid>/<publishedfileid>/manifest.json     every file with its SHA-256 and the folder hash
<root>/<appid>/<publishedfileid>/content/          the item as it was uploaded
<root>/<appid>/<publishedfileid>/ATTRIBUTION.txt    author, app and the maps it was composed from
<root>/archive.db.json                             the index of what is archived
<root>/archive_report.json, archive_report.md      what the last run did
```

The composition list is read from the archived `references.json`. It matters for review: an item built from another
map carries that map's units, art and scripts, so a mirror of it redistributes that map too.

## Mirror staging is manual and reviewed

Archiving is automatic; republishing is not.

```pwsh
dotnet run --project tools/WorkshopArchive -- stage-mirror --archive-root D:\RealmArchive `
    --appid 5372260 --item 1234567890 --reviewer <your steamid64> `
    --note "Creator deleted the item; archive copy reviewed 2026-11-18" --confirm
```

This copies the reviewed item into `<root>/mirror/<id>/` with `mirror.json` (original item, author SteamID, original
title, licence version and the clause relied on, reviewer, date, uploading account, review note) and `ATTRIBUTION.txt`.
A person then uploads it as a Workshop item from the dedicated mirror account — never the partner account, because that
account is the one an infringement notice or a strike reaches. Nothing uploads automatically: duplicate listings confuse
players and dilute the original author's page, and an item the developer has not personally reviewed must never be
published. A mirror stays on Steam; there is no Realm mirror site, download page or public mirror API.

`takedown --item <id> --reason "..." --confirm` records the request and removes the mirror, not the archive copy.

## Scheduling

Run weekly from the developer's machine or a small CI job, and run a full pass immediately before the demo AppID is
unpublished: an item missed in that final week is gone rather than late. The tool is not a server and has no scheduler.

## Secrets

Both variables are read from the environment or a local, gitignored file, and the tool refuses to run without them.
Never commit a key, an account name or a STEAMCMD session. The tool never touches the game's runtime, a player's Steam
account or the partner account's credentials.
