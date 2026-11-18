# Bypassed approvals

Decisions taken on the developer's behalf while working through `prompts/SteamWorks/`, in the spirit of
"make your best judgement without stopping to ask". Each entry names the task it belongs to, the call I made, and why.
Anything here is a candidate to revisit during the collaborative testing phase.

## 05 — Lobby registry removal

- **The whole `Realm.AdminServer` went with the lobby registry.** The prompt scopes this task to the lobby half and
  defers accounts (11) and distribution (10) to their own tasks. Tasks 11, 13 and 22 delete those services anyway, and
  every remaining endpoint in that project existed to serve a world the later tasks remove, so splitting the deletion
  across three commits would have meant three rounds of editing files whose only future was the bin. It is one commit
  instead, and the checkpoints after this one are gameplay-visible ones.
- **Deleted `Realm.Client/servers.template.json` and the `ServersConfig`/`ServersConfigHelper` surface.** Nothing reads
  a lobby server any more, and the template is the only thing that would tell a contributor there is one to run.
- **`HostLobbyStage` is now two steps.** `TestingNetwork`, `OpeningPort`, `VerifyingNetwork` and `RegisteringLobby`
  described work Steam does on its own, so the host's progress card shows the map loading and the host taking its seat.

## 06 — Relay, NAT traversal and reachability

- **`RestrictiveNetworkDialog` is deleted rather than reworded.** Its body text explained UPnP, port forwarding and
  NAT types, and its only action copied Realm's own network diagnostics; keeping the frame and rewriting the words
  would leave a dialog whose reason to exist was the subsystem being removed. A failed join now states what the join
  said went wrong, or "Steam could not connect to the host".
- **The "seed map files" setting is deleted, not hidden.** It volunteered this machine as a distribution seeder, which
  no longer has a directory to advertise to. Its checkbox node left `SettingsMenu.tscn` with it.
- **`PeerLinkStatistics` is kept** as the prompt asks; Steam's transport feeds it.

## 07 / 08 / 09 — Workshop publishing, consumption and identity

- **A Workshop item's identity is one JSON string in the item's own metadata field** (`WorkshopMapMetadata`), plus two
  key-value tags (`mapName`, `build`) for lookups. Steam's tags are a fixed vocabulary configured per app, and the
  identity needs version, build, content hash, author and dependencies, so a metadata string is the part of the item
  that is ours. Nothing else has to be registered anywhere.
- **Publishing creates the item the first time by asking the Workshop whether an item already states the map's name**,
  instead of storing an item id in `metadata.json`. That keeps the map format — and its schema — unchanged, and it
  means an item published from another machine is still found and updated rather than duplicated. The cost is one
  query per publish; if a map is renamed, a new item is created, which is the honest reading of a rename.
- **The upload is a canonical copy of the workspace, not the workspace itself.** `MapArchiveHelper.StageCanonicalCopy`
  writes exactly the files an exported `.rmap` carries, so the author's `bin/`, `obj/`, `.vscode/`, history and keys
  never reach Steam.
- **The item preview is a PNG converted from the map's own `thumbnail.webp`**, written beside the staged folder rather
  than into it: Steam reads PNG and JPEG, and the downloaded map should not carry the storefront's copy of its picture.
- **No test-version channel.** Steam Workshop has one item per map with version history, so the publish dialog's
  "publish as test version" switch and the maintainer-key note are gone; publishing updates the item in place.
- **Greenlight is gone with the admin server.** Steam's own moderation and reporting is the review surface (task 13).
- **`MapData.FromDto` became `MapData.FromWorkshop`.** Ratings are Steam's up/down votes; the creator is the Steam
  account that owns the item; the thumbnail is Steam's preview URL.
- **`AssetPackService` came back on top of `MapProvisioningService`**, because it was only ever a named wrapper around
  it and the editor's art pack still needs a distribution channel. It is now an ordinary Workshop item whose identity
  is marked `IsAssetPack`, which a browse filters out.

## 18 — Attribution

- **Asset authors are read from the assets themselves** (`RealmMetadataHelper.ExtractAuthor`) instead of the deleted
  `asset_author` endpoint. The signing step already walks every referenced hash, and each file's metadata states who
  made it, so attribution survives with no service and no extra network call.
- **Map authorship is verified against the key the map carries.** The lobby previously asked the admin server for the
  published key and signature; it now recomputes the unsigned metadata hash and checks the Ed25519 signature in the
  file against the `author_key` beside it. A map that names no key still shows its stated author, unverified.

## 16 — Creator identity

- **The creator panel reads Steam, and nothing is cached.** `CreatorDiscoveryService` asks the Workshop for the items a
  SteamID owns in this AppID on each open, and the avatar comes from `GetMediumAvatar` per SteamID. A cached copy would
  be a profile database with a lifetime, which is the thing the migration removes.
- **Other items are queried for the current AppID only.** A SteamID is global, so the query states the app; a creator
  who also publishes for another game shows only the maps they made for Realm.

## 17 — Workshop archive

- **The archive tool is a separate console project (`tools/WorkshopArchive`), not part of the shipped game.** It uses the
  same `SteamWorkshopService` read paths, writes a reviewable JSON record plus the item's files, and takes the partner
  publisher key from the environment so no key reaches the repository or a build.
- **No archive is published or served anywhere.** The output is a local folder an operator keeps; `README.md` in the tool
  says why, because the obvious next step for a "mirror" is a download page and that is exactly what is forbidden.

## 19 — AGENTS.md

- **The old reachability section is replaced whole, not amended.** Its vocabulary (classes, cold probes, translated
  ports) is the vocabulary of a subsystem that no longer exists, and keeping any of it would read as an instruction.
- **The new guards are written as prohibitions with the reason attached**, so a future change has to argue with the
  reason rather than just with a rule. The section deliberately repeats none of the per-task design: it names the
  boundary and leaves the design in the task files.
- **`GameBuildNumber` became provenance rather than a gate** in the map format section. The equality gate was what made
  every published map unplayable after a release, and leaving the old sentence in place would have reinstated it by
  documentation.

## 15 — Cleanup

- **The donate button, its popover node and its URL are deleted** from the main menu, with the `DONATE` localization key
  following it on the next build. The task forbids a donation surface in the game, and a footer link to the project's own
  donation page is that surface even though it funds the developer rather than a creator.
- **The moderation model and its download step are deleted.** `scripts/fetch-ml-model.ps1` fetched a Hugging Face ONNX
  model for a toxic-language filter, and no code read it any more; the workflow step and its `SHIP_TOXIC_FILTER_MODEL`
  switch went with it. Steam's Workshop reporting is the moderation surface now.
- **`tools/rig/` is deleted.** It existed to make network claims about a lobby registry and reachability classes that no
  longer exist, and AGENTS.md no longer points at it.
- **The admin server's build steps are deleted from CI** (publish, archive, upload, `servers.json` injection) along with
  its launch and task entries and its `.gitignore` lines.
- **`NameNormalizationHelper` and its suite are deleted.** Every member was unused: the account service was its only
  caller, and map-name normalisation has its own implementation in `MapProvisioningService`. Its `AnyAscii` and
  `Fastenshtein` references left `Realm.Shared` with it (`Fastenshtein` remains where the editor's fuzzy asset matching
  uses it).
- **29 orphaned `.cs.uid` files are deleted.** They pointed at sources deleted by earlier tasks, including
  `MapDistributionServer`, `CreatorDiscoveryService` and `WindowsFirewallService`.
- **`servers.json` is dropped from the two map-folder file filters.** It was excluded from map discovery back when the
  client carried one; nothing writes it now.
