# 02 — Move Multiplayer onto Steam's Peer-to-Peer Network

> Read `00-Architecture-and-Task-Order.md` first. It defines the Steam-first assumptions, the player-identity/infrastructure split and the task order.

**Depends on:** 01.
**Unblocks:** 03, 04, 06.

## User story

As a player, I want to host a match without forwarding a port, enabling UPnP, answering a firewall prompt, or sharing an IP address, and I want players on restrictive networks to connect through Steam's relay without either of us knowing.

As the developer, I want one transport that gives me authenticated peers, relay fallback and connection quality, so the automatic NAT, punch, relay and direct-IP code can all be deleted.

## Context

The authority currently listens with Godot's ENet peer and the client connects to an IP and port. The frame protocol, snapshot fragmentation, repair ring, presentation timeline and map scripting are all transport-independent and must stay. This task changes only how bytes reach the peer.

ENet does not disappear entirely in this task, but what remains of it stops being multiplayer. The host's own screen reaches its authority over a loopback socket today, and that socket is ENet; it stays as internal IPC bound to the loopback address. What is retired is the remote path on top of it: the listener that binds beyond loopback, the host's port choice, and the client's "Join Private Lobby via IP Address" dialog, together with the guest seat that path admits. There is no second way to reach a match, and no screen asks a player for an address.

**The loopback decision is recorded here, not left as a carve-out.** `ISteamNetworkingSockets::CreateSocketPair` is present in the pinned Steamworks.NET 2024.8.0 and is the documented in-process loopback, so the host's own seat could ride the same transport as every remote peer. Two designs are therefore defensible and one has to be chosen in writing:

- **Keep ENet for the loopback seat (the default).** It already works, it is one less thing to call across a process boundary, and it keeps the Steam callbacks out of single-player, which must run with no Steam client at all. This choice must be stated in the task report, not implied by the absence of a change.
- **Move the loopback seat onto `CreateSocketPair`.** That deletes the ENet peer, the IPC carve-outs in tasks 02, 06 and 19, and the "loopback stays ENet" caveat in every later task, at the cost of making the local seat depend on Steam initialisation.

Do not leave the question open, and do not start the second option halfway. The default is the first: it is smaller, and a single-player match with no Steam client must keep working either way.

**This replaces the transport, not just the socket.** Godot's ENet peer gives the match unreliable-sequenced delivery, and the snapshot path depends on it: a delta states only the units that changed since the description before it, the client holds a description that arrived ahead of a hole and asks for the run, and the repair ring answers that hole. Steam's peer-to-peer sockets offer reliable-ordered and unreliable messages, and an unreliable message carries no ordering guarantee at all. The receive path therefore has to enforce what ENet used to provide, and the match-critical sync layer has to be re-measured rather than assumed. The remote ENet path survives exactly long enough to be the control: it carries the same game frames over the transport the game was tuned on, so a regression can be attributed to the transport instead of to the game. It is deleted in this task once the comparison in "The measurement that decides this migration" is accepted, and it is never visible to a player in the meantime.

Steam's peer-to-peer sockets provide the connection, authentication, encryption and Steam Datagram Relay. They are not a Godot `MultiplayerPeer`, and they are not a drop-in replacement for the ENet transport. Do not attempt to wrap them in `SceneMultiplayer` or `MultiplayerPeerExtension`; `MultiplayerPeerExtension` is a GDExtension-facing class and would add a native module for no benefit. Send the game's frames as Steam messages and keep the game's own frame codec.

## Architecture to implement

1. **One frame contract, two channels.** Extract the frame send/receive contract currently embedded in `ClientNetworkChannel` so the host's loopback connection and the Steam connection both satisfy it. The snapshot, command, lobby and repair frames must not be duplicated per transport. `ClientServerSession` chooses the channel; the rest of the client does not know which one is in use.
2. **Authority owns the Steam listener.** A hosted authority creates a Steam P2P listen socket as the host's Steam user and accepts remote peers. The host's own screen keeps its loopback connection to the authority. A single composite server transport presents both peer sets to the existing sink with disjoint internal peer IDs. The listener follows the match lifecycle: it is created when a networked match starts and released when the match is paused, while the Steamworks context stays up for the process.
3. **Client joins by SteamID.** A joining client creates a Steam P2P connection to the host's SteamID. There is no IP, port, punch ticket or relay plan in the join path. The host's SteamID comes from the Steam lobby (task 03), not from a registry response. Until task 03 lands, this task may be exercised with a development-only host SteamID entry; it must not ship and task 03 replaces it.
4. **Steam identity is the peer identity.** The transport owns the mapping between the internal integer peer ID the simulation already uses and the peer's SteamID64. Expose a lookup so the authority can record the SteamID on the seat and validate lobby membership. Do not change the simulation's integer peer contract wholesale. Allocate the internal ID from a monotonic counter and never reuse one within a match; when a Steam connection closes, clear its snapshot repair ring and any frames still queued for that ID before the number can be handed out again. A recycled ID that inherits a departed peer's ring entries or queued frames delivers one player's world to another.
5. **Reliability maps to Steam's flags, and ordering is enforced by the receiver.** Reliable frames (lobby state, commands that must arrive, baselines, repairs) use Steam's reliable send. Snapshot deltas use Steam's unreliable send, with the send flags that let a message leave immediately rather than being buffered for coalescing with the next one. Steam has no ordered-unreliable mode, so the snapshot receive path must drop any description whose sequence is at or below the newest one it has already accepted, and the repair path must not report a superseded description as a hole. Do not assume ENet's unreliable-sequenced behaviour survives the swap. Never move a delta onto the reliable stream because of size; the existing fragmentation layer handles size.
6. **Fragmentation stays.** Keep the snapshot fragment and reassembly code. Steam's message limit is large but finite; keep the existing fragment size, or tune it below Steam's limit, without changing the wire contract that the tests protect.
7. **Connection lifecycle.** A peer is connected when Steam reports the connection is established, and disconnected with the Steam reason code when it closes. Translate the useful reasons into the existing disconnect/notice messages so the lobby can say "connection lost", "host closed", or "Steam relay unavailable" rather than a numeric code.
8. **Link statistics.** Replace ENet peer statistics with Steam's real-time connection status for round-trip time and packet loss. Steam reports ping and connection quality but not jitter as a separate figure; derive jitter from the variance of recent round trips and label it as derived in the diagnostic line instead of presenting it as a measurement. The lobby's connection-quality display should read the same kind of numbers it does today, sourced from Steam.
9. **Handshake.** Keep the existing protocol/build handshake and the existing ticket verification until task 11. Steam has already authenticated the connection, so do not add a second cryptographic identity layer. The hello may carry the SteamID, but the transport's identity is authoritative.
10. **Steam relay is invisible.** Steam chooses direct or relayed paths automatically. Do not expose a relay toggle, a relay plan, a relay volunteer or a relay status in the UI or the logs beyond a normal diagnostic line.
11. **Retire the remote ENet path.** The listener stays in the build only until the comparison in "The measurement that decides this migration" is accepted, so it can carry the control run. Then delete it in this task: the part of the listener that binds beyond the loopback address, the host's "Allow direct IP" option and port field, the client's "Join Private Lobby via IP Address" button and its address dialog, and the guest-seat admission path that existed for a peer whose Steam ticket could not be verified. Keep the loopback socket the host's own seat uses, and bind it to the loopback address explicitly, or a machine with a permissive firewall is still listening for a player the game no longer admits. No seat is ever a guest afterwards: a peer is a SteamID or it is not connected.

## The measurement that decides this migration

Before anything is deleted, record how the game feels on the transport it was tuned on, then compare. This is an A/B of the game, not a benchmark of Steam's network, and it is the only comparison that answers the question actually being asked.

Do not frame it as Steam versus your relay. The volunteer relay in this repository has never carried a match and exists only on an unmerged branch, so it is neither a baseline nor a working alternative: comparing an unused experiment against a shipped library would tell you nothing about whether the game is playable. The choice in front of you is Steam's transport, or the ENet path the game runs on today.

**Question 1 — gates everything: is the game as fluid on Steam as it is on ENet, over an ordinary connection?**

1. **Record the baseline first, on the unchanged build.** A host on an ordinary connection — not behind carrier-grade NAT, which is question 2 — with six to eight joiners on different networks, a fixed map and a fixed duration. Record descriptions sent and received, fragment counts and reassembly failures, repair requests and baseline recoveries with their reason codes, the presentation delay each peer settled at, every frame the cursor ran dry, round trip and loss.
2. **Repeat the identical scenario over Steam P2P**: same map, same seat count, same duration, same network shapes.
3. **Accept on feel first, with more than one person and without telling them which build is which.** "A player must not be able to tell" measured by the person who wrote the change is not a measurement. Run two builds — ENet and Steam P2P — with the same map and seat count, and ask two or three people who were not told which is which to say whether either felt worse, and to name the one they would keep. A felt regression is a failure even when the numbers look reasonable; if it feels the same or better, accept it and continue. If only one tester is available, say so in the report rather than presenting a single opinion as a comparison.
4. **If it feels worse, diagnose before concluding.** A Steam callback pump that runs once per tick, a delta left buffered for coalescing instead of sent immediately, a fragment size above a comfortable Steam datagram, a seat count nobody has tested, and the authority-identity pivot in task 00 are all fixable, and none of them is evidence against Steam. Bound the effort, then decide.
5. **If it cannot be made acceptable within that bound, stop and report.** Do not delete the remote ENet path and do not proceed to the deletion tasks. "Steam's networking is not good enough for this game" is a legitimate outcome of this task, and this is the only place it can be discovered.
6. **State the outcome in one line**: what was compared, the delay each peer settled at on each transport, the frames the cursor ran dry, and whether a player would notice.

**Question 2 — informational, gates nothing: can a host behind carrier-grade NAT be played against through Steam's relay?**

The current build ships punch and reachability measurement — a real, maintained subsystem, described in `AGENTS.md` and covered by five test suites — but the relay needed to seat a host behind carrier-grade NAT is unfinished, exists only on an unmerged branch, and has never carried a match. So those players join other lobbies and cannot host. That is the status quo, and it is what this question is measured against: the measurement is not whether punch works, it is whether Steam's relay seats a host that our own never could.

- If a relayed host is playable at lobby size, hosting behind carrier-grade NAT becomes a capability the game can advertise, and it is the strongest single reason to prefer Steam over finishing a custom relay.
- If it is not playable, nothing is lost. Those players keep today's behaviour, the game says so plainly, and **no deletion task is blocked on this answer**. Do not revive the relay branch on `main` as a fallback.
- Task 06 deletes nothing of the relay from `main`, because none of it is there: it lives on its branch in git history, so the option to finish it survives that task untouched.

Run question 2 when a teammate or volunteer with a restrictive network is available. It is a capability check, not a precondition, and it must not be run instead of question 1.

## In scope

- The authority's Steam listener and the client's Steam connection.
- The composite transport that keeps the host's loopback seat working.
- Frame delivery mapping, connection callbacks, disconnect reasons and link statistics.
- The transport factory that hides the identity model, so the task 00 pivot stays small.
- The comparison run against the remote ENet path, and the deletion of that path once it is accepted.
- Logging that states the SteamID, the path type Steam chose, and the disconnect reason.

## Verification (a transport replacement, not a socket swap)

The ECS sync layer is the most timing-sensitive code in the project and this task changes the transport beneath it. Verify it with numbers, not by inspection, and report what was measured.

1. **Baseline first, on the unchanged build.** Run the loopback host with joiners over the existing remote ENet path, on a fixed map and a fixed duration, and record the figures listed in question 1.
2. **Repeat the same scenario over Steam P2P** with accounts on separate machines (or VMs), and compare the same numbers.
3. **Repeat once through the Steam relay** (or from a network where a direct path is unlikely), and once on a deliberately lossy link if one can be arranged.
4. **Treat a broken invariant as a defect in this task, not as tuning:** a missing description the ring still holds is repaired rather than baseline-recovered, the cursor never moves backwards, the presentation delay does not grow without bound, and an out-of-order delta is dropped instead of applied.
5. **Record the control figures before deleting the path that produced them.** After this task the remote ENet path is gone, so its numbers are the only copy of the comparison that will exist. Put them in the task report, not in a comment.
6. **Do not add permanent tests for this.** Write the check for this change, run it, report the result and delete it, per the repository's test policy. Name the runs that could not be performed, such as a second account, a second machine or a relay-only network, instead of claiming them.

## Out of scope

- Creating or browsing Steam lobbies (task 03). This task and task 03 form one networking cutover; online play may be unavailable between them, and that is expected.
- Deleting the old relay, reachability and NAT code (task 06).
- Deleting the loopback socket between the host's screen and its authority. ENet remains the loopback IPC transport after this task; only the part that binds beyond loopback is retired here.
- Removing accounts or ticket verification (task 11).
- Changing the snapshot format, repair ring, delta compression or presentation timeline.

## UX

- Hosting no longer shows a port, a firewall prompt, a UPnP result, a NAT type or a public address.
- No screen asks for an IP or a port, and there is no "Join by IP" action to find.
- The lobby room shows connection quality from Steam, not a relay/NAT classification.
- A failed connection says something a player can act on: "Steam could not connect to the host", "The host closed the lobby", "Steam is offline". It does not mention punch, relay or endpoints.
- The lobby browser loses its "Join Private Lobby via IP Address" button and the create panel loses its direct-IP option and port field. Nothing replaces them; a player with no Steam is told why multiplayer is unavailable rather than offered a worse way to connect.

## Acceptance criteria

- Two Steam accounts on different networks can host and join a match through Steam, with no port forwarding.
- The match runs at the same tick and snapshot cadence as before.
- The host's own seat still connects over loopback and is unaffected.
- A peer behind a restrictive NAT connects through Steam's relay when a direct path fails.
- The lobby shows Steam-sourced round-trip and loss numbers.
- No public ENet listener is opened for a hosted Steam lobby, and no IP or port is advertised.
- No ENet listener is bound beyond the loopback address, and no screen offers to join or host by address and port.
- No seat can exist without a verified Steam identity.
- A description that arrives after a newer one is dropped, and the repair path does not report it as a hole.
- Internal peer IDs are not reused within a match, and a closed peer's repair ring and queued frames are cleared before its number is handed out again.
- The pre-change and post-change measurements are reported, with every scenario that could not be run named explicitly.
- The loopback decision is recorded in the task report: which transport carries the host's own seat and why, with `CreateSocketPair` either adopted or explicitly declined.
- The snapshot fragmentation and repair tests still compile and, if run, pass unchanged.
- The authority logs the peer's SteamID and the path type Steam selected.

## Pitfalls

- **Do not route the host's own seat through Steam.** It is not mechanically forbidden — GameNetworkingSockets will connect a process to its own SteamID and relay the traffic over the network to itself — but it spends a relay hop on a seat that could use a loopback socket, on a code path nothing else exercises. Keep the host's seat on loopback unless the `CreateSocketPair` decision above is taken deliberately.
- **Do not use `SceneMultiplayer` for this.** Its RPC layer assumes a `MultiplayerPeer`, and a Steam peer is not one. Send frames directly.
- **Do not poll Steam callbacks only when a frame is expected.** Pump them every frame or tick on both processes, or connections and disconnects will appear late.
- **Do not put snapshots on the reliable stream to avoid fragmentation.** That recreates the head-of-line stall the existing design explicitly avoids.
- **Do not block the simulation tick on Steam callbacks, connection waits or synchronous I/O.** Poll the transport and deliver frames through the existing non-blocking path.
- **Do not expose Steam's relay as a custom feature.** It is an implementation detail of the transport, not a lobby setting.
- **Do not leave the remote ENet path bound while it is still in the build for the comparison.** A listener that accepts a remote peer is a second multiplayer path, even when no UI offers it; bind it only for the control run, and delete it when the run is accepted.
- **Do not add a third path to replace the one being deleted.** No port prompt, no LAN mode, no "connect to a friend's IP if Steam fails". A session Steam cannot identify is a session the game cannot seat, moderate or keep in step.
- **Do not assume Steam preserves ENet's unreliable-sequenced delivery.** An unreliable Steam message may arrive out of order; the receiver is what makes the snapshot stream ordered, and the repair path must not mistake a superseded description for a missing one.
- **Do not reuse a peer ID inside a match.** The simulation's peer ID is a small integer with a repair ring and a queue behind it; recycling one without clearing them sends a departed peer's world to a new player.
- **Do not invent a guest identity.** Without the direct-IP path there is no peer Steam has not authenticated, so there is no case in which the game needs a name it cannot verify.
- **Do not create a second frame codec for Steam.** If the frame contract needs a small adapter, adapt; do not fork the protocol.
- If the authority cannot initialise Steamworks as the host user, implement the pivot documented in task 00 behind the transport factory. Do not fall back to a custom relay.
- The repository pins Steamworks.NET 2024.8.0, and every entry point this task needs is present in it: `SteamAPI_InitEx`/`ESteamAPIInitResult`, `CreateListenSocketP2P`, `ConnectP2P`, `SendMessageToConnection`, `ReceiveMessagesOnConnection`, `GetConnectionRealTimeStatus`, `CreateSocketPair` and the `k_nSteamNetworkingSend_*` flags. Do not upgrade the package as a precaution. If a documented call really is missing, upgrade the managed package and the native library together and document the version pair; never mix them.
