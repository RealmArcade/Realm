# Bypassed approvals — judgement calls made without asking

Every decision here was taken autonomously while implementing `prompts/SteamWorks/`, per the
instruction not to stop for approval. Each entry states what was decided, why, and what to revisit.

---

## A. Scope and ordering decisions

### A1. The multi-version launcher was deleted in task 01, not 09A

`01`'s requirement 12 deletes `GameUpdateService`, and its only other consumer was the main menu's
version dropdown (`GetOtherInstalledVersions`). Leaving the dropdown behind would have been exactly
the "orphaned, unreachable UI" defect the review flagged. So the whole launcher surface went in
stage 1:

- `GameUpdateService` deleted (both the GitHub release poll and the installed-versions helper).
- `MainMenu.CreateVersionSelector` reduced to a plain version label; `PopulateVersionDropdown`,
  `OnVersionSelected`, `_versionDropdown` and `_outdatedLabel` deleted.
- The `DOWNLOAD...` button and `https://github.com/RealmArcade/Realm/releases` deleted.
- `LobbyManager.GetBaseGameDirectory` / `GetVersionExecutablePath` deleted.
- `LobbyBrowser.ShowVersionMismatchPopup` → `ShowDifferentBuildNotice` (one line, no relaunch).
- `ReplayListPanel.ShowVersionMismatchPopup` → `ShowUnreadableReplayNotice` (one line, no relaunch,
  no download), which is the replay decision `00`'s register asks for.
- `LobbyCreate.ShowMapBuildMismatchModal` no longer offers LAUNCH/DOWNLOAD.

Task 09A therefore has nothing left to delete on this surface; it owns the generation work only.

### A2. A third relaunch path was left as a plain message

`LobbyRoom.StartFlow.ShowVersionMismatchPopup` only ever showed a popup and returned to the lobby; it
had no relaunch or download action. Its text was left as-is (it already says the connection was
aborted) rather than renamed, because nothing about it was a non-Steam path.

### A3. Task 20's local half was committed in stage 1, as its own instruction requires

`DiagnosticsService`, `DiagnosticsRuntime` (new autoload in `project.godot`), the settings row and the
one-line crash offer on the main menu all ship with stage 1. Requirement 6 (hosted error service) is
left undecided, which is what the task asks for.

### A4. The join path was not rewired in this checkpoint

See section C. Task 02's transport is built and compiling; the join and seat flow still use the
existing path. This is why the branch is described as "transport built" rather than "task 02
complete".

---

## B. Technical decisions

### B1. The host's own seat stays on ENet loopback

`CreateSocketPair` was declined, as `02`'s default. Single-player and the editor must work on a
machine with no Steam client at all, and making the local seat depend on Steam initialisation would
break that. ENet therefore survives only as the loopback IPC, bound to `127.0.0.1` explicitly by
`CompositeServerTransport`.

### B2. Peer ids are allocated by the composite, not by ENet

`CompositeServerTransport` re-maps both links onto a monotonic counter (loopback from 1, Steam from
1000) that never repeats inside a match, so a departed peer's repair ring and queued frames can never
be read as a new peer's. This is stricter than ENet's own recycling and is what `02` requirement 4
asks for.

### B3. Frame kind travels in a four-byte header

Godot's RPC layer states the frame kind for the ENet paths; a Steam message is opaque bytes. Rather
than fork the protocol, `SteamFrameCodec` prefixes one little-endian 32-bit kind, shared by both
sides. The frame *contract* (`NetworkFrameKind` + payload) is unchanged, which is what `02`
requirement 1 protects.

### B4. Unreliable sends use `k_nSteamNetworkingSend_UnreliableNoDelay`

Steam has no ordered-unreliable mode, so a frame leaves immediately rather than being coalesced. The
receiver-side ordering rule (drop a description at or below the newest accepted sequence) is **not yet
implemented** and is part of the remaining work in section C.

### B5. Steam's link figures are labelled for what they are

`PeerLinkStatistics.PacketLossPercent` carries Steam's connection-quality figure expressed as a
percentage, because Steam publishes no packet-loss count. Jitter is derived from the variance of
recent round trips. Both are stated in the type's own documentation rather than presented as
measurements the game took.

### B6. The build number is the only lobby filter

The prompts say a lobby matches on "game build and protocol", but this repository has no protocol
version constant — the handshake is keyed on `RealmVersion.GameBuildNumber`. Adding a protocol
constant would have been a new concept threaded through the handshake for no gain, so the Steam
lobby filter and metadata use `GameBuildNumber` alone. Revisit only if the handshake gains a real
protocol version.

### B7. A code-gated lobby is granted two extra members

`SteamLobbyDirectory.CodeGatedHeadroom = 2`, per `03` requirement 1 and the pitfall about sizing a
code-gated room to its seats. The close-once-seated step still needs wiring.

### B8. `steam_api64.dll` is now ignored for the authority too

The build copies the native library beside the authority executable. It is a build artefact, not a
source file, so `Realm.ListenServerHost/steam_api64.dll` was added to `.gitignore`; the tracked copy
stays in `ThirdPartyBinaries/`.

### B9. `MainMenu` keeps a plain version label

Removing the dropdown entirely would have removed the only on-screen statement of which build is
running, which support needs. The label now reads `Version: {0}` with `RealmVersion.GameBinaryVersion`.

---

## C. What remains, precisely

### C1. Stage 2 is unfinished: tasks 03 and 04 are scaffolded, not wired

Built and compiling:

- `Realm.Shared/Network/SteamFrameCodec.cs` — the shared frame header.
- `Realm.ListenServerHost/Network/SteamServerTransport.cs` — the authority's P2P listener, identity
  map, link statistics, route logging.
- `Realm.ListenServerHost/Network/CompositeServerTransport.cs` — loopback + Steam as one peer set.
- `Realm.Client/Network/SteamClientLink.cs` — the client's P2P connection.
- `Realm.Client/Network/SteamLobbyDirectory.cs` — create / browse / join / leave / metadata /
  membership callbacks / chat / invite dialog.
- `ClientNetworkChannel` routes `Send` and `SendUnreliable` through the Steam link when one is open,
  and pumps it every frame.
- `IServerTransport` gained `Poll`, `TryGetPeerSteamId` and `TryGetPeerPathType` as default members;
  `ListenServerHost.Tick` polls the transport before reading any frame.
- `ListenServerEntry` builds the composite transport and refuses a networked match with no Steam
  identity.

Still to do, in this order:

1. **`LobbyManager.PublishPreparedLobbyAsync`** — replace the reachability + `RegisterLobbyWithRegistryAsync`
   body with `SteamLobbyDirectory.CreateAsync`, then write the metadata block (`MapKey`, `MapVersionKey`,
   `MapHashKey`, `EngineGenerationKey`, `GameBuildKey`, `SeatsKey`, `HostSteamIdKey`, `RequiresCodeKey`)
   and set `ActiveLobbyId` to the Steam lobby id. The host SteamID written there is
   `GameContext.Steam.SteamId`.
2. **`LobbyManager.JoinLobbyAsync`** — parse the Steam lobby id, call `SteamLobbyDirectory.JoinAsync`,
   read `HostSteamIdKey`, and replace `ClientServerSession.TryJoinLobby(ip, port, …)` with the channel's
   `TryConnectToSteamHost(hostSteamId, …)` followed by the same handshake. The whole punch / NAT /
   registry-report branch set in `LobbyManager.Joining.cs` becomes unreachable and is deleted by task 05.
3. **`LobbyBrowser.FetchLobbiesFromRegistry`** — replace the HTTP body with
   `SteamLobbyDirectory.BrowseAsync()` and build `LobbyData` from `GetData` / `GetMemberCount` /
   `GetLobbyOwnerSteamId`. Delete the ping, reachability and IPv6 columns with it.
4. **Authority admission** — `ListenServerEntry` passes the Steam lobby id in the start command;
   `ServerLobby` admits a peer only when `transport.TryGetPeerSteamId` names a member of that lobby, or
   the peer arrived on loopback. This is `04` requirement 2 and is the rule that makes every seat a
   Steam account.
5. **Snapshot ordering on Steam** — the receive path must drop a description whose sequence is at or
   below the newest accepted one, and the repair path must not report it as a hole
   (`02` requirement 5, and its "do not assume ENet's unreliable-sequenced delivery" pitfall).
6. **Join code** — the authority holds it, refuses seating until presented, never writes it to Steam
   metadata, and clears on demand (`04` requirement 12).
7. **Roster reconciliation** from `SteamLobbyDirectory.MembershipChanged`, and lobby chat moved onto
   `SteamLobbyDirectory.ChatMessageReceived`.
8. **The measurement** in `02` — baseline on ENet, comparison on Steam, accepted before the remote
   ENet path is deleted. Nothing was measured; no second account or second machine was available.

### C2. Tasks not started

05, 06, 06A, 07, 08, 09, 09A, 10, 11, 12, 13, 14, 16, 17, 18, 19, 21, 22, and the requirement-6
decision of 20. Deliberately not begun rather than half-deleted: every one of them either deletes a
system whose Steam replacement is in C1, or sits behind those deletions in the documented order.

---

## D. Things noticed that the task set does not mention

- **`OpencodeCliInstaller`** (`Realm.Client/Services/OpencodeCliInstaller.cs`) fetches
  `api.github.com/repos/sst/opencode/releases/latest` and installs the editor's agent tool. It is a
  developer tool installer, not a game update channel, so it was kept — but it is the last
  `api.github.com` call in the client and `15`'s grep sweep will hit it. It should be listed as
  deliberate rather than removed by a later sweep that assumes every GitHub call is residue.
- **Two GitHub links remain by design**: the BUG REPORT button (`MainMenu`) and the
  `DMCA/Copyright Infringement Report` link (`GameOver`). `06A` should point the second at the new
  process rather than letting a second takedown route appear.
- **`_seedMapFilesChk`** in `SettingsMenu` is a seeder toggle and is removed with task 10, not now.
- **`LobbyManager.RegistryServers` / `RegistryServerUrl` / `ServersConfigHelper`** are the levers `15`
  should grep for; roughly ten client call sites reach the old platform through them.
