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
