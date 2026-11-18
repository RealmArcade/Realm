# 17 — Archive and Mirror Workshop Maps

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 07 and 06A.
**Unblocks:** nothing; it is an operational tool.

## User story

As the developer, I want an automated archive of every Workshop item published for Realm, with its metadata and content, so a creator deleting or privatizing a map cannot erase it, and so I can restore a reviewed map to a Realm-owned mirror.

## Context

Steam hosts the content and the creator can delete it with `DeleteItem`, which cannot be undone. The UGC license (task 06A) grants Realm a perpetual mirror right that survives deletion. Steam does not provide a built-in "download everything for my AppID" archive tool. SteamCMD can download one item at a time, and the Steam Web API can list items. The archive is a local developer tool, not a service and not part of the game.

## Before building: survey existing tools

1. **`pairofcrocs/steam-workshop-archiver` already covers most of this.** It is a self-hosted web app that runs whole-Workshop jobs for an AppID, checks each item's last-updated time for incremental downloads, lists items through `IPublishedFileService/QueryFiles` with an optional Web API key, downloads through SteamCMD or DepotDownloader, supports scheduled jobs, and flags removed items while keeping their local copies. Evaluate it first. If its license and feature set fit, self-host it and document the setup instead of writing a new archiver. Do not vendor it into the game repository.
2. If it lacks a required feature, write a thin companion tool around it rather than a second full downloader. The likely gaps are content-addressed deduplication across items, a manifest the game's package reader can verify, and the manual mirror-staging workflow.
3. Other candidates to evaluate: SteamCMD `workshop_download_item`; `WorkshopDL`; `SteamWorkshopDownloader`; `DepotDownloader` (does not handle Workshop items); the Steam Web API `IPublishedFileService/QueryFiles` and `ISteamRemoteStorage/GetPublishedFileDetails`.
4. The download engine should be SteamCMD, which is Valve's supported path. Do not use third-party downloader websites for the canonical archive.
5. Write the survey result into the task report, including the tool chosen, its license, and why.

## Requirements

1. **Choose the smallest path.** If `steam-workshop-archiver` fits, deploy it and write only the integration: the archive root, the schedule, the metadata export and the mirror-staging workflow. If it does not, put a small tool under `tools/WorkshopArchive/` or an existing `tools/` convention. Either way it is a developer tool, not shipped with the game and not referenced by the game's solution projects. It may be a .NET console app or a script; choose what fits the repository.
2. **Listing, for every AppID the game publishes under.** Use the Steam Web API with a key from `STEAM_WEB_API_KEY`. During the demo era a map exists as two items — one in the demo's Workshop and a dormant one in the official Workshop (task 07) — and they are archived separately, because they are different items with different IDs even though one creator owns both:
   - `IPublishedFileService/QueryFiles` (v1) with `appid`, `page`, `numperpage=100`, `return_short_description=true`, `return_vote_data=true`, `return_tags=true`, `return_children=true`, `return_metadata=true` and `return_details=true`.
   - Page until fewer than 100 results.
   - Record the published file ID, title, description, owner SteamID64, `time_created`, `time_updated`, `visibility`, `banned`, tags, votes up/down, file size, `hcontent_file`, preview URL, `creator_app_id`, `consumer_app_id`, `filename`, `file_type`, subscriptions, favorited, views, children IDs and the raw JSON.
3. **Storage.** Use an `--archive-root` outside the repository, for example an external drive. Layout: `<root>/<appid>/<publishedfileid>/` with `metadata.json`, `manifest.json`, `content/`, `LICENSE.txt` and `ATTRIBUTION.txt`. Keep a content-addressed copy of each distinct `hcontent_file` so an update does not duplicate unchanged files. A small SQLite index (`archive.db`) or a JSON index is fine; choose the simplest option that supports incremental runs and queries.
4. **Download.** Use SteamCMD:
   `steamcmd +force_install_dir <staging> +login <archive_account> +workshop_download_item <appid> <publishedfileid> +quit`
   Use a dedicated archive account that owns the app, not the developer's personal account. Steam Guard must be handled interactively once; document the setup. Copy the downloaded folder into the archive and verify it before deleting the staging copy.
5. **Incremental.** Download only when `hcontent_file` or `time_updated` changed. `--dry-run` prints what would change. `--full` re-verifies and re-downloads everything. `--item <id>` handles one item.
6. **Deleted, private and banned items.** If an item is no longer returned by the query but exists in the archive, mark it `missing` and keep the content. If `GetPublishedFileDetails` returns a result code, record it. Never delete an archived item because Steam no longer lists it.
7. **Integrity.** After download, walk the folder, compute SHA-256 per file and a deterministic folder hash, write `manifest.json` with the file list, sizes and hashes, and verify on every run. Mark corrupt items for re-download. Add a `verify` command that loads each archived map with the game's package reader where that is practical.
8. **Report.** Print a summary: total, new, updated, unchanged, missing, failed, bytes downloaded and archive size. Write `archive_report.json` and `archive_report.md`. Exit non-zero on failures.
9. **Mirror is manual, reviewed and uploaded from a separate account.** Archiving is automatic; republishing is not. A `stage-mirror <id>` command copies a reviewed item into a `mirror/` folder with a `mirror.json` (original item ID, author SteamID, original title, license version and clause relied on, reviewer SteamID, date, the uploading Steam account and the review note) and a short attribution file. A human uploads it as a Workshop item for the AppID from the dedicated mirror account, never the partner account. Do not publish a mirror as a `.rmap` on a Realm site or anywhere else outside Steam: Realm does not host mirrors off-platform, Steam hosts the item, and `.rmap` is the format a creator shares their own map with. The uploading account is the one an infringement notice or an account strike reaches, and it still publishes under this AppID, so it narrows the notice path rather than putting the app out of reach. Do not auto-republish every item; duplicate listings confuse players and dilute the original author's page.
10. **Takedown.** A `takedown <id>` command records a request and removes the mirror, not the archive. Honor low-effort mirror requests per policy. Keep the archive copy unless counsel says otherwise.
11. **No CDN purge.** There is no Realm CDN to purge. The tool may prune the mirror on a schedule; it never deletes a Workshop item.
12. **Scheduling, and the one deadline that is not a schedule.** Run weekly, or daily, from the developer's machine or a small CI job. The tool is not a server. Document the command and the account setup. Do not add it to the game. Retiring the demo AppID is a hard cutoff rather than a schedule: run a full pass and verify it immediately before the demo is unpublished, because an item missed in the final week is gone rather than late.
13. **Secrets.** Read `STEAM_WEB_API_KEY` and the SteamCMD account from environment variables or a local, gitignored config. Never commit either. The tool must refuse to run if they are missing.
14. **Legal hooks.** The archive writes the license version and attribution into each item. It records the creator's SteamID as the author. It does not strip attribution. It does not archive items the developer has been told to remove, unless counsel says to keep a record. It also records the composition an item was built from: read the archived `references.json` and `composed/` and put the referenced map names into the item's index and its `ATTRIBUTION.txt`, because a composed map republishes another creator's assets and scripts under this item, and the mirror reviewer needs to see whose work an item actually carries before deciding whether to review it.

## UX

This is a developer tool, not a player-facing feature. It may be a CLI or the adopted tool's local web UI. Where a CLI is used, the output is a clear report and a non-zero exit on failure; where the existing archiver is used, its job report is the output.

## Out of scope

- Building a custom web admin UI for the archive. The adopted tool's local UI is fine.
- Automatic republishing.
- A public mirror API.
- Deleting Workshop items (the developer cannot delete another user's item).
- Changing the game's map format.

## Acceptance criteria

- A dry run or the adopted tool's preview lists all items for each AppID the game publishes under.
- A full run archives metadata and content, deduplicates identical content and writes manifests and a report.
- A second run downloads nothing unchanged.
- A deleted item remains in the archive and is marked missing.
- The tool refuses to run without the API key and archive account.
- The tool never touches the game's runtime or the player's Steam account.
- A mirror can be staged only with explicit confirmation and attribution.
- The survey section records whether `steam-workshop-archiver` was adopted, its license, and why it did or did not fit.
- If adopted, the archive layout and metadata export are documented, and the tool is not vendored into the game repository.

## Pitfalls

- SteamCMD downloads to `steamapps/workshop/content/<appid>/<id>`; copy it out and verify before cleaning up.
- SteamCMD login may need Steam Guard; use a dedicated account with a saved session, not the developer's.
- `GetPublishedFileDetails` may not return deleted items; do not rely on it for history.
- Do not use the game's `ISteamUGC` client API for bulk archiving; it is tied to a running client and subscriptions.
- Do not republish mirrors automatically.
- Do not store the archive in the repository; it contains large binaries and other creators' content.
- Do not assume `hcontent_file` is stable across updates; it changes when content changes.
- Do not archive only `.rmap`; the Workshop item is a folder. Archive the folder as uploaded.
- Do not treat the archive as a license to re-upload. The license permits it; the manual review decides whether it is appropriate.
- Do not treat a composed item as its uploader's alone. Its `references.json` and `composed/` name the map whose content it carries, and a mirror of it redistributes that map too.
- Do not upload a mirror from the partner account. Keep the mirror account separate, record which account published each mirror, and do not describe that separation as legal immunity.
- Do not mirror an item the developer has not personally reviewed, and do not queue mirrors for automatic upload.
- Do not host mirrors outside Steam. There is no Realm mirror site, archive download page or public mirror API, and a mirror never leaves Workshop as a `.rmap`.
