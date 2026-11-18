# Review of the SteamWorks task set

Reviewed against `origin/main` (`dd2bff3`), the pinned Steamworks.NET 2024.8.0 package, and the
Steamworks/Valve documentation. Everything below is either *verified* (I checked the file, the
symbol or the doc) or marked **unverified**. Severity is about what happens if you run the task
without fixing it.

---

## Resolution log

The owner reviewed this document and the task set was corrected accordingly. Where this review and
a task file disagree below, **the task file is now the newer text** — the findings remain as the
reason the corrections exist.

Applied in the task set:

- `00` — one branch (`steam_rewrite`), one pull request, with stages as checkpoints instead of a
  row-per-branch table; the official AppID exists and is linked as the demo's base game; monetization
  is the Inventory Service plus a cosmetic DLC, with `ISteamMicroTxn` explicitly excluded; the
  removal register gained the version launcher, the GitHub update path and cross-version replays;
  the `lobby_handover` figure and the `20` placement are corrected.
- `01` — `GameUpdateService` and the GitHub download path are deleted here; the AppID mechanism is
  named (`SteamApplicationIds`, `steam_appid.demo.txt` / `.retail.txt`); the two-account test note now
  states that the second Steam client must be signed into a second account; the acceptance criterion
  no longer claims the direct path is gone at this stage.
- `02` — the `CreateSocketPair` loopback decision must be recorded; the "upgrade if P2P is missing"
  pitfall is closed against 2024.8.0; the acceptance bar names multiple blind testers; question 2 now
  credits the shipped punch/reachability subsystem.
- `03` — a code-gated lobby is sized above its seats and closes to new members once seated; the
  metadata limits are stated as per value and per key, with no invented total.
- `05` / `15` — `Realm.Client/servers.template.json` is the committed file, not `servers.json`; the
  ENet note points at 02 and 06 as its owners; the residue sweep gained `GameUpdateService`,
  `api.github.com`, `GetVersionExecutablePath`, `versions/` and the server-config helpers.
- `06` — punch and reachability are described as shipped and maintained, with only the relay
  branch-only; `IPv6_Lobby_Decision.md` and `Seed_Node_Setup.md` are named at the repository root;
  all nine test files are listed.
- `06A` — the terms review document is **created**, and the existing GitHub takedown path is kept.
- `07` — the write passes the target AppID to `CreateItem` / `StartItemUpdate`, stated as the single
  cross-AppID exception, and the unreleased-official-Workshop write is validated on day one.
- `09A` — the launcher is described as coded-but-never-exercised and is deleted; replays are
  supported only while their format is readable; the CI guard compares against a committed baseline
  and gains an agent skill in `.agents/skills`; `MapUpgradeService`'s two editor call sites are named.
- `13` / `22` — the star popup and metrics files are named; all nine network test files and all
  three solution files are named.
- `19` — the launcher, the update path and the replay rule are added as guards, and the test-suite
  paragraph is rewritten.
- `21` — Inventory Service and cosmetic DLC are the two mechanisms; `ISteamMicroTxn` is excluded.

Not applied: §4.6. Task 17 already scopes the archiver as a local developer tool rather than a
service, which is the substance of the point; revisit it only if the tool is actually adopted.

The §6 items stand unchanged, because they are things to measure rather than things to write down as
facts: what a demo client's write into an unreleased app's Workshop does in practice, whether the
authority's listen socket is reached when the same SteamID is initialised twice locally, Steam's
real leaderboard budget, and the current snapshot fragment size against
`k_cbMaxSteamNetworkingSocketsMessageSizeSend`.

---

## Verdict

The set is unusually good: the platform facts are mostly right, the "one transport, one identity"
line holds all the way through, and the tasks avoid the trap of rebuilding a service next to Steam.
The file names it gives for deletion are real — I checked `LobbyEndpoints`, `LobbyRegistry`,
`PeerRegistry`, `GeoIpService`, `HostSignaling`, `JoinerPunchRelay`, `JoinFailureFeedback`,
`SeederRegistry`, `ClusterPeerService`/`ClusterPeerSync`, `ContentPruner`, `ManifestVersionPruner`,
`Realm.Shared/Reachability/*`, `Realm.Tools.Admin/*`, `MapUpgradeService`, `MapAttributionService`,
`LocalAuthority`, `ServerControl`, `MapReferenceResolver`, `MapMetadataMerger`, `MapScriptComposer`,
`MapComposition.g.cs`, `metadata.partial.json`, `Attribution_ImportedFromMap`,
`Attribution_SourceMap`, `SteamScriptNames`, `PresentationKind.Steam*`, `SetPeerSteamId`,
`realm_game_lobby_id`, `GameOver.Metrics.cs`, `LobbyRoom.MapAuthorship.cs`,
`LobbyDirectConnectHarness.Ipv6.cs`, the `Realm.AdminServer` `ProjectReference` in
`Realm.Network.Tests.csproj`, and the `Publish Realm.AdminServer` / 7z / upload-artifact steps in
`.github/workflows/build.yml`. `Realm.Shared/Network/Relay/*`, `RelayPool`, `RelayModels`,
`/relays/*`, `LobbyManager.Relays`, `PeerRelayVolunteer`, `RelayPeerDemux`,
`RelayPoolMembershipTests`, `RelayWireContractTests`, `LobbyCreate.Reachability.cs` and
`docs/Manual_Test_Volunteer_Relay.md` are correctly described as branch-only. The relay branch
counts are exact (110 commits, 177 files).

There is one class of defect that matters more than all the detail below, and it is the class you
said you care about: **three shipped, player-visible features are removed by this plan without any
task mentioning them.** See §1.

---

## 1. Features that would be removed silently

You asked to get back to a close approximation of the current game and to care only about *removed*
features. These are the removals no task owns.

### 1.1 The multi-version launcher — and replays with it

This is not a "previous plan" (as `09A` frames it). It is live behaviour on `main`:

- `Realm.Client/Network/LobbyManager.cs` — `GetBaseGameDirectory()` and
  `GetVersionExecutablePath(targetVersion)` resolve `…/versions/<build>/<exe>`.
- `Realm.Client/UI/LobbyBrowser.cs` — `ShowVersionMismatchPopup` offers "RELAUNCH" into another
  installed build, or "DOWNLOAD" → `https://github.com/RealmArcade/Realm/releases`.
- `Realm.Client/UI/MainMenu.cs` — `PopulateVersionDropdown` / `OnVersionSelected` relaunch another
  installed version from the main menu.
- `Realm.Client/UI/LobbyCreate.cs` — `ShowMapBuildMismatchModal` does the same for a map whose
  build differs.
- `Realm.Client/UI/ReplayListPanel.cs` — `ShowVersionMismatchPopup` does the same for a replay.

So today a player can join a lobby made on another build by downloading that build from GitHub, and
can watch a replay recorded on another build the same way.

`09A` changes the *gate* (`MapSelectionService.IsBuildMismatch`, `LobbyCreate` line ~340) so the
popup never fires. That leaves the versions dropdown, `GetVersionExecutablePath`, four relaunch
paths, their `.tscn`/localization keys and the GitHub download links orphaned and unreachable, with
nobody told to delete them. And **replays appear nowhere in the 23 tasks at all** — not in `15`'s
smoke test, not in `00`'s removed list, not in `10`/`13`/`22`.

**Recommendation.** Rewrite `09A`'s Context to say this is current behaviour, not a discarded plan,
and add an explicit clause: delete the versions dropdown, `GetBaseGameDirectory` /
`GetVersionExecutablePath`, all four `ShowVersionMismatchPopup`/relaunch paths and the GitHub
download links, because a build difference is now a Steam-update message and no old build exists.
Then make a decision, in writing, about replays. Either:

- **replays stay, same-generation only** — a replay header records `EngineGeneration`; older ones
  show one line ("recorded on an earlier version of Realm") instead of a relaunch; or
- **replays are dropped** — say so in the release notes and in `00`'s removed list.

Silence is the one option that produces a half-deleted feature.

### 1.2 The GitHub update check and its DOWNLOAD button

`Realm.Client/Services/GameUpdateService.cs` polls
`https://api.github.com/repos/speige/Realm/releases/latest`, called from the main menu, and
`MainMenu.cs` (~lines 420–450) shows an "Outdated" label plus a `DOWNLOAD...` button that opens
`https://github.com/RealmArcade/Realm/releases`.

This contradicts `00` directly ("There is no non-Steam storefront", "Steam keeps them in step") and
`15`'s sweep does not list it as a term to grep. It is also the mechanism `03` requirement 8 assumes
Steam provides ("a screen that has not restarted after an update is told to restart") — currently the
game tells the player that itself, from GitHub.

**Recommendation.** Delete `GameUpdateService`, the outdated label and the download button in `01` or
`02` (whichever lands first), and add `GameUpdateService`, `IsNewerReleasePublishedAsync`,
`api.github.com` and `github.com/RealmArcade/Realm/releases` to `15`'s search-term list. Note also
that the two slugs disagree (`speige/Realm` vs `RealmArcade/Realm`), so any surviving GitHub link
should be audited deliberately rather than kept by accident.

While you are there: `MainMenu` has a BUG REPORT button and `GameOver.cs` opens a
`DMCA/Copyright Infringement Report` issue on GitHub. Those are dependencies on GitHub as a support
and takedown surface. `06A` says "add a takedown contact" — it already exists; say "keep it and point
it at the new process" rather than letting a second one appear.

---

## 2. Statements that are inaccurate

### 2.1 "The relay, punch and reachability code has never carried a match" (`06`)

Only the **volunteer relay** is branch-only and never ran. The **punch and reachability** code is on
`main`, is what `AGENTS.md`'s "Lobby Reachability (measured, never certified)" section describes as the
shipped listing policy, and the most recent commit on `main` (#103) is about it. That is also why
`Realm.Network.Tests` has five reachability suites.

The deletion is still right — Steam replaces it — but the justification is wrong, and the wrong
justification is load-bearing: "it never worked, so removing it gives up nothing" invites the agent to
skip the one thing that actually needs checking (can a host behind carrier-grade NAT still be played
against through SDR, which `02` question 2 measures). Reword to: punch/reachability is shipped and
maintained, Steam replaces it, and the capability question is question 2 in `02`.

### 2.2 `Terms/Steam_Migration_Terms_Review.md` does not exist

`00` cites it as an existing document and `06A` says "Update" it. I checked the repository root and
`Terms/`: the folder holds `RealmEngine_License.txt`, `RealmPlatform_ToS.txt`,
`RealmPlatform_UGC_License.txt` and `TRADEMARK.md`. Under `00`'s own rule this is a documentation
defect, but here it is a work item: `06A` should say **create** it, and state what it must contain
(clauses that no longer describe the product, resolved items, items for counsel).

### 2.3 `01`'s acceptance criteria cannot pass at `01`'s merge point

- Row 1 merge condition: "the old direct path still works because row 3 has not removed it."
- Requirement 10: the Join-by-IP button, the direct-IP host option, the port field, the guest seat
  and the address dialog "are removed in task 02 and task 06".
- Acceptance criterion: "No screen offers a way to join or host a match without Steam, and no address
  or port prompt remains anywhere."

Those cannot all be true in the same build. Worst case, the merge gate fails forever and the agent
"fixes" it by deleting the direct path early with no measurement behind it. Pick one: either `01`
ships a build where the no-Steam screen says multiplayer needs Steam while a Join-by-IP button still
works (honest, and row 1's merge condition already says so), or `01` is folded into row 3.

### 2.4 `22` names three of the nine test files that go

`Realm.Network.Tests` contains `ReachabilityCheckTests`, `ReachabilityContractTests`,
`ReachabilityLocalCallerTests`, `ReachabilityNatMatrixTests`, `ReachabilityRouterTests`,
`JoinCapabilityTests`, `JoinPhaseMachineTests`, `LobbyJoinabilityTests` and `Ipv6EndpointTests`.
`22` names three. Say "every test file in `Realm.Network.Tests` whose subject is reachability,
joinability, join phases or IPv6 endpoints" and list all nine.

Relatedly, `AGENTS.md`'s "the suite is deliberately small and holds only contracts…" paragraph names
the reachability probe, lobby identity wire and joinability policy. `19`'s term list has
`Reachability` but not "the suite description"; add an explicit instruction to rewrite that
paragraph with the surviving contracts.

### 2.5 Small precision fixes

| Where | Says | Actually |
|---|---|---|
| `22`, `15` | `Realm.Client/Realm.Client.sln` | The root build is `Realm.slnx` (`build.yml` runs `dotnet build Realm.slnx`); `Realm.Client/Realm.Client.sln` and `Realm.ListenServerHost/Realm.ListenServerHost.sln` also exist. Name all three. |
| `05`, `15` | `servers.json` | There is no committed `servers.json`. `ServersConfigHelper` loads `servers.json` / `servers.template.json` from a search path at runtime. Phrase it as "the `ServersConfig`/`ServersConfigHelper` surface and the runtime config file it reads". |
| `15` | — | Add the two best residue levers to the sweep: `ServersConfigHelper`, `GetDefaultServerUrl`, `GetAccountAuthorityUrl`, `RegistryServerUrl`. They are how ~10 client call sites (`MapStorageService`, `AccountService`, `CreatorDiscovery`, `GameOver.Metrics`, `MapProvisioningService`, `LobbyManager`) reach the server, so they survive every individual feature deletion and are the cheapest way to find the last caller of a dead endpoint. |
| `00` | `lobby_handover`, "117 commits" | 90 commits / 149 files ahead of current `main`. Also, "concurrently about 20k lines" — the diff is ~149 files; worth stating as a file count since the line count is not verifiable from the branch alone. |
| `06` | delete `IPv6_Lobby_Decision.md` | It is at the repository root, not under `docs/`. Same for `Seed_Node_Setup.md`, which `10` covers generically as a seeder runbook — name it. |
| `09A` | "`MapUpgradeService` … is only ever invoked from the editor (`MapEditorHUD.MapUpgrade`)" | Also invoked from `Realm.Client/UI/MapEditorHUD.MapLoad.cs:46`. Name both. |
| `01` | "Provide a `steam_appid.txt` only in local development output" | The mechanism already exists: `Realm.Client/Services/Steam/SteamApplicationIds.cs` (`FullGameAppId = 5372260`, `DemoAppId = 5537420`) and `Realm.Client/steam_appid.demo.txt` / `steam_appid.retail.txt`. Say what those are for and that the rule is "the process initialises under the AppID it was launched with", so the agent does not invent a third mechanism. |
| `05` | "Do not delete the ENet client needed by 'Join by IP'; it is not the registry" | True at row 4, but `02` and `06` delete that path later. Add "(removed by 02/06)" so the next task does not read it as a survivor. |
| `03` | "Keep the total well under Steam's per-value limit" | 8,192 bytes is the *per-value* limit (`k_cubChatMetadataMax`), 255 chars is the key limit, and Valve does not document a total. Rephrase. |
| `13` | "Remove the Realm star rating…" | The UI half is `Realm.Client/UI/GameOver.ReviewPopup.cs` (star picker at match end) and `GameOver.Metrics.cs`. Name them, or an orphan star widget survives the backend deletion. |

---

## 3. Platform facts, checked

**Correct, verified against Valve's docs:** the 50-result cap on a lobby list request with no
pagination (`AddRequestLobbyListResultCountFilter` bounds it further); 250 lobby members; the
documented statement that Steam does not support matchmaking between a demo and its full game;
`App Publish Permissions` (see below); `DeleteItem` being owner-only and unprompted; up/down votes
rather than stars, with `m_unVotesUp` / `m_unVotesDown` / `m_flScore`; `GetQueryUGCChildren` for a
collection; `ISteamUGC` (not SteamCMD) for author-facing publishing.

**Correct, verified in the pinned package** (Steamworks.NET 2024.8.0 — I inspected the assembly's
metadata): `SteamAPI_InitEx`, `ESteamAPIInitResult`, `SteamNetworkingSockets.CreateListenSocketP2P`,
`ConnectP2P`, `SendMessageToConnection`, `ReceiveMessagesOnConnection`,
`GetConnectionRealTimeStatus`, `k_nSteamNetworkingSend_UnreliableNoNagle`, `SendLobbyChatMsg`,
`GetLobbyChatEntry`, `SetLobbyJoinable`, `SetLobbyMemberLimit`, `InviteUserToLobby`,
`CreateQueryUserUGCRequest`, `CreateQueryAllUGCRequest`, `GetItemInstallInfo`, `GetNumSubscribedItems`,
`SetUserItemVote`, `GetQueryUGCChildren`, `m_unVotesUp`, `m_flScore`, `SubmitItemUpdate`,
`StartItemUpdate`, `DeleteItem`, `SetItemTags`, `FindOrCreateLeaderboard`, `UploadLeaderboardScore`,
`SetRichPresence`, `StartPurchase`, `GetAllItems`, `kNumUGCResultsPerPage`.

Consequences:

- `02`'s pitfall "if the P2P socket APIs are missing from that version, upgrade" can be closed: they
  are present in 2024.8.0. Keep the "do not mix managed and native versions" warning.
- `SteamGameServer_Init` is not a managed wrapper member; the task-00 pivot should name
  `Steamworks.GameServer.Init` (plus `LogOnAnonymous`).
- `ISteamInventory::UpdateItemDefs` is not a thing and the prompts never claim it is —
  `IGameInventory/UpdateItemDefs` is the publisher-authenticated Web API, and item definitions are
  normally uploaded as ItemDef JSON in the partner site's Inventory Service. `21` says exactly that;
  keep it that way. `SetItemPaymentRules` is likewise `IWorkshopService`, not a client call.

### 3.1 `App Publish Permissions` — real, but `07` omits the part that breaks the build

The field exists on the **target** app's Workshop Configuration, and both apps need Cloud quota for
preview images — `07` says both. What `07` does not say is the API rule: a demo client writing into
the official Workshop must pass the **official AppID** as the consumer, i.e.
`CreateItem(officialAppId, …)` and `StartItemUpdate(officialAppId, publishedFileId)`, not the running
AppID. Without that, the write silently lands in the wrong Workshop or fails.

This is also the one documented exception to the rule stated firmly in `09`, `13` and `16` ("never
query or subscribe under the other AppID"), so state the exception where the rule is stated, or the
implementer will conclude the two requirements contradict and pick one.

Two smaller notes on `07` requirement 21: the guide describes this for a separate *publishing tool*
app, not specifically for demos, so the "publish one real item on day one" instruction is the right
guard — keep it prominent. And the ability to revoke the permission at cutover is a good catch.

### 3.2 `ConnectP2P` to your own SteamID is attempted, not refused — and `CreateSocketPair` exists

Valve's GameNetworkingSockets logs `Connecting P2P socket to self … Traffic will be relayed over the
network` and proceeds down the P2P path. So `01`'s note ("two game instances under one account share a
SteamID and cannot form a P2P connection") is wrong in mechanism, and the practical advice that
follows (use `-master_ipc_name_override` + `-userchooser`) only works because you log the second
client into a **second account**. Say that explicitly; the sentence as written tells a tester that a
second Steam client under the same account is the setup, which is the one configuration that will not
give a meaningful peer.

More interesting: `ISteamNetworkingSockets.CreateSocketPair` is in the pinned package and is the
documented in-process loopback. That means the host's own seat could ride the same Steam transport
as every remote peer, with one frame contract and two channels — which would delete the ENet loopback
IPC entirely, along with the `02`/`06`/`19` carve-outs about "ENet survives only for loopback".

That is a larger change than this review should force, and keeping ENet is defensible (it is already
working, and it is one less thing to call across a process boundary). But it is the kind of
simplification your stated goal favours, so `02` should at least record the decision: either
"loopback stays ENet because X" or "loopback uses `CreateSocketPair` and ENet is gone".

### 3.3 `ISteamMicroTxn` is not a serverless option

`00` says "the sale runs through the Steam Item Store or `ISteamMicroTxn`". The Item Store /
`StartPurchase` path is genuinely serverless — Valve documents the inventory service as usable
"server-less", with the client calling `StartPurchase` and handling `SteamInventoryResultReady_t`.
`ISteamMicroTxn` is not: it requires a developer purchasing service that holds a publisher key and
posts `InitTxn` / `FinalizeTxn` to Steam (Steam does not need to call *you*, but you do need the
backend). That is a service, so it contradicts the whole document. Delete the `ISteamMicroTxn`
alternative from `00` and keep the Item Store as the only path.

---

## 4. Design and sequencing inconsistencies

1. **`09A` requirement 16 will not run in CI as written.** It compares the generated import surface
   with "the previous release tag". GitHub Actions checks out at depth 1, so tags are absent by
   default, and this repository's tags are `0.0.1_Pre-Alpha_Rev_*` rather than a release semver, so
   "previous release tag" is undefined even locally. Use a committed baseline
   (`MapApiSurface.baseline.json`) regenerated by an explicit command, with the diff reviewed in the
   pull request. Same for `09A` requirement 15's "append-only relative to the previous release tag".
2. **`03` sizes the Steam lobby to the seats, then lets code-less members in.** Requirement 1 sets
   `MaxMembers` from the map's playable slots; requirement 6 says a member may enter the room and see
   it but cannot take a seat until the code is entered. With the room sized to the seats, code-less
   members occupy the capacity the code holders need, so a public event lobby can be filled by people
   who cannot play. Pick one: size the room above the seats for a code lobby, or set
   `SetLobbyJoinable(false)` once the expected players are in, or state that the code gate is only
   meaningful on a friends-only lobby.
3. **`20` lands in three different places.** `00`'s task-order table puts it last; `00`'s branch table
   makes the local half row 2; the task says "ship the local half early". The branch table is the one
   that will be followed, so make the task-order section say the same thing.
4. **`02`'s acceptance bar is unfalsifiable with one person.** "A player must not be able to tell
   which transport is underneath them" is right in spirit, but a single tester measuring their own
   memory of the previous session will pass anything. Name 2–3 testers, or a blind A/B (two builds,
   same map, participants told neither name).
5. **`00`'s "What this migration deliberately removes" is not complete enough to sign off.** It
   covers platforms (accounts, registry, relay, CDN, reviews, admin) but not the client-side
   removals: the version switcher, the GitHub update/download path, replays across builds, the
   post-match star popup, MMR/custom rank, map maintainers, reserved names, guest seats. Since your
   acceptance test for this migration is "only removed features would bother me", promote that
   section to a flat, exhaustive register of player-visible removals — one line each, with where the
   replacement lives — and make it the thing you read at the end against `15`'s smoke test. Right now
   no single artefact lets you do that check.
6. **`17` and "no service" pull slightly apart.** `pairofcrocs/steam-workshop-archiver` is a
   self-hosted web app. Adopting it is fine as a developer tool, but say "local developer machine
   only, never publicly reachable, never in the shipped repo", or the adoption reads as standing up
   infrastructure in a document whose premise is deleting it.

---

## 5. Recommended order of edits

1. `00` — add the three missing removals to the register, delete `ISteamMicroTxn`, fix the
   `lobby_handover` figure, and align the `20` placement.
2. `01` — name `SteamApplicationIds` / `steam_appid.*.txt`; fix the two-account test note; resolve the
   acceptance-criterion contradiction; delete `GameUpdateService` and the GitHub update path here or
   in `02`.
3. `02` — close the "upgrade if P2P is missing" pitfall as verified; record the ENet-loopback
   decision and mention `CreateSocketPair`; soften the acceptance bar to something testable.
4. `06` — reword "never carried a match"; name `IPv6_Lobby_Decision.md` at the root and
   `Seed_Node_Setup.md`.
5. `07` — add the `CreateItem`/`StartItemUpdate` target-AppID rule and mark it as the single
   documented cross-AppID exception.
6. `09A` — rewrite the Context as current behaviour; add the version-launcher and GitHub-download
   deletions; replace the tag comparison with a committed baseline; add the replay decision.
7. `13` — name `GameOver.ReviewPopup.cs`; `15` — add the residue grep terms and the missing file
   names; `22` — name all nine test files and all three solution files.
8. `06A` — "create `Terms/Steam_Migration_Terms_Review.md`"; "point the existing takedown link at the
   new process".

---

## 6. Not verified

- Whether the demo-to-full-game write through `App Publish Permissions` behaves as documented for a
  demo that is *unpublished* at cutover. The documentation covers separate publishing apps, not
  demo retirement; `07`'s "publish one on day one" guard is the correct response, keep it.
- Whether the authority's inbound P2P connection reaches its listen socket when the same SteamID is
  initialised in two local processes. This is exactly the thing task `00` says to validate rather
  than assume, and I found no third-party confirmation either way — treat the pivot as likely needed.
- Steam's per-app leaderboard count budget (`12` requirement 4). I could not find a documented
  number; keep the rule ("do not create one per map") and drop the appeal to a documented budget
  unless you can cite one.
- `k_cbMaxSteamNetworkingSocketsMessageSizeSend` and the current snapshot fragment size, so `02`'s
  "keep the existing fragment size, or tune it below Steam's limit" is untested — measure both before
  changing it.
