# Network Host Resilience — Task Ticket and Checklist

| Field | Value |
| --- | --- |
| Type | Epic |
| Area | Networking (join handshake, reachability, ports, lobby feedback, relay) |
| Priority | High — this is the difference between "can host" and "cannot host" for a large share of players |
| Status | Partially implemented — the same-network guard (C9, C10) is in; every other item below is open |
| Size | Large; sliced into five milestones below |
| Depends on | `fix/p2p-connection-diagnosis` review (this ticket supersedes and completes it) |
| Excludes | IPv6 support — tracked separately in `docs/ipv6_task.md` |

---

## Background

Realm is peer-to-peer: the hosting player runs the authoritative process, and every other player connects to that machine over UDP. Everything below follows from one finding from the review of `fix/p2p-connection-diagnosis`:

**A lobby being published does not mean a player can join it.**

The publish gate asks a registered node to reach the lobby's game port. That proves exactly one thing — that one datagram got in from that one source address — and NAT state is per destination. So a lobby can be "verified" and still be unjoinable by anybody else, which is what happens behind a consumer VPN (registers, never joins) and was also the state of the branch in review for hosts without UPnP.

Three separate defects compound it:

1. **The host no longer punches the joining player.** The join request states a client port of `0`, so the relayed punch is dropped on the floor. A host behind a filtering router therefore never sends anything toward the joiner, and the joiner's packets are discarded on arrival. On `origin/main` this worked whenever the client's socket held 8999.
2. **"Same LAN" is inferred from equal public IPs.** Two machines behind one CGNAT, one office NAT, or the same VPN exit node satisfy that test and are then sent to each other's *private* addresses, which are unreachable across those networks.
3. **A registry on the host's own machine can verify its own lobby.** The probe is bound to loopback and the service deliberately falls back to the request's source address, so a local node probes `127.0.0.1`, answers itself, and publishes the lobby pointing at `127.0.0.1`. That is a false pass that no amount of care on the host side prevents, and it also means a developer can never reproduce a real reachability failure locally.

On top of that, the branch switched lobbies to a system-chosen port, which is only half-migrated: 8999 is still assumed by the direct-connect default, the help copy, the seeder and map-download ports, and a static port forward — the most reliable hosting path that exists — now needs a new port each time.

This ticket fixes the handshake, makes reachability an honest classification rather than a false promise, gives the host real feedback when players cannot get in, makes the whole failure reproducible on one machine so it can be diagnosed rather than guessed at, and adds a relay for the players no amount of punching can help.

---

## Status

Landed on `fix/p2p-connection-diagnosis`:

- **A1** — the registry states a report ticket in the join answer; the joining screen reports itself from the socket its attempt holds; the node reads the endpoint that report arrived from and hands it to the host, which punches it. The join request no longer states a port the screen cannot know.
- **A2–A6, A8** — a local network is decided by this machine's own interface table instead of equal public addresses; every join field is read defensively; the budget is one attempt with a full window, retried only on silence; the blocking bind retry is gone; one punch constant and one datagram; an external port measured for another local port is cleared.
- **A11** — the register route refuses an endpoint that is not publicly routable, with its own error code.
- **C9, C10** — the same-network refusal and the client-side defence, already landed before this ticket, are unchanged and still covered.
- **Publishing a listed lobby no longer blocks the room** — the host enters the lobby room as soon as the lobby exists, edits the map's settings while the check runs, is told in the room's chat when the lobby is listed, and keeps the lobby as a direct one when the listing cannot be confirmed.
- **J1, J2 (Tier A), J3–J7, J10** — `Realm.Tools.Cli join-lobby` resolves a lobby through a registry node, reports this machine, punches, and waits for the authority's answer, with per-phase timings, per-phase exit codes and `--expect phase:outcome`. It states that no seat was taken.
- **Rig** — `tools/rig/realm-rig.sh` gives a host and a player real NAT paths in namespaces on one machine, and the authority runs headless on Linux. See `docs/rigs/README.md`.

Still open: A7, A9, A10, B1–B5, C1–C8 and C11–C13, D1–D7, E1–E5, F1–F9, H1–H4, I2–I8, J8, J9, J11, and the end-to-end lobby run inside the rig (which needs a compiled map).

---

## User Stories

**As a player hosting a lobby**, I want to know whether players can actually get in, and to be told what to change when they cannot, instead of publishing a lobby that silently fails.

**As a player joining a lobby**, I want a failure to tell me which step failed and what to do about it, and to be able to tell the host when their lobby is unreachable.

**As a player on a network that blocks all inbound traffic** (carrier-grade NAT, mobile hotspot, locked-down office), I want to host anyway, even if it costs me a little extra latency.

**As the operator of the lobby server**, I want to add relay capacity without running anyone's simulation, and without the relay becoming an uncontrolled bandwidth sink.

**As a player on IPv4-only connectivity**, I want none of this to change how my working lobbies and joins behave.

---

## Goals

1. A listed lobby is either genuinely joinable, or is published with an accurate reachability class.
2. Hole punching works from both sides, including when the joiner's ENet socket holds a system-chosen port.
3. A player who shares no reachable path is told precisely why, at the point of failure.
4. The host learns when real players fail to reach the lobby, with enough detail to act.
5. A host behind a network that permits no inbound traffic can opt into being relayed.
6. IPv4 behaviour for the setup that works today is unchanged.
7. Any join failure can be reproduced, diagnosed and re-tested on one machine, without orchestrating two computers, and the tool that does it reports the same phases the game does.

## Non-Goals

- IPv6 (separate ticket).
- Moving the simulation off the host, or running map scripts anywhere but the authority.
- Any change to snapshot cadence, size, compression or fragmentation.
- Any map-specific rule, match setting, or lobby option tied to a particular map.
- Relay by default. It is opt-in, badged, and capped.
- Changing the map-seeding feature beyond not advertising unreachable seeders.

---

## Definitions

| Term | Meaning |
| --- | --- |
| **Open** | A node reached the game port without the host punching it first. Only this class deserves "verified". |
| **Punch-only** | A node reached the game port only after the host punched it. Joinable when both sides punch; this is the common home-router case. |
| **Unverified** | No node reached the game port. |
| **Endpoint** | A family + address + port a lobby can be reached at, judged on its own. |
| **Phase** | The join step that failed. One vocabulary for every surface: `registry`, `resolve`, `punch`, `answer`, `admission`, `session` (see J4). |
| **Mapping** | Whether the host's router reuses one public port for every destination (endpoint-independent) or makes a new one per destination (port-dependent). Carrier-grade NAT and many VPN exits do the second, and no punch fixes it (C13). |
| **Rig** | A repeatable arrangement of machines, VMs or VPNs a failure is reproduced on. |

---

## Already Implemented (do not redo)

The same-network guard (C9 and C10) landed before this ticket was finalised. It is described here so it is extended rather than rebuilt:

- `Realm.Shared/Reachability/ReachabilityAddresses.cs` holds the one rule for "could a player elsewhere be sent to this address at all": loopback, wildcard, link-local, RFC 1918, the carrier-grade shared range, IPv6 unique-local, multicast and unparseable text all answer false, and it fails closed.
- A registry node refuses to start a check when the caller reaches it from inside its own network (`ReachabilityEndpointHandlers.Begin`, status `ReachabilityStatusCodes.CallerOnOwnNetwork`), and logs the refusal. The `allowSameNetworkCaller` parameter is opt-in and is set only by the test harness.
- The client reports that refusal as `HostReachabilityFailure.ProbeFromOwnNetwork`, with its own lobby-create help text rather than the port-forward text.
- `LobbyJoinability.Decide` additionally requires the measured endpoint to be publicly routable, which is the client-side defence that works even against a lobby server that has not been updated.
- `Realm.Network.Tests/ReachabilityLocalCallerTests.cs` covers the rule, the refusal, and the publish decision.

---

## Keep From the Branch (it got these right)

`fix/p2p-connection-diagnosis` is not to be reverted wholesale. These parts are correct and the fixes below build on them:

- The joining screen punches from the socket its ENet peer already holds, instead of from a second socket bound to the same port. That removed a real Windows bind collision.
- Reconnection is gated on having been connected before and on not already connecting, and channel-level `ConnectionFailed` / `ServerDisconnected` events replace a retry loop that could never succeed.
- A reconnection punches the lobby's game port from the socket it just created (`PunchJoinedLobbyFromGamePort`). That is the same principle as the own-socket punch, applied to a new socket, and it is correct.
- The reconnect cadence was widened from 250 ms to 1.5 s over 240 attempts, which is the right shape for punching (sustained attempts rather than a stampede). Keep it, but state the total budget as a number instead of leaving it to be multiplied by hand.
- The registry carries the host's real local port, so a peer on the same network no longer guesses it.
- The Windows firewall allowance is program-scoped, which is what a lobby that may bind a system-chosen port needs.
- The publish path's mapping keepalive, and the reachability matrix tests that codify "a game port is judged only by whether an outside node reached it".

---

## Defects in the Branch (what the fixes must correct, not repeat)

Each row is a defect found while reviewing `fix/p2p-connection-diagnosis`. The item column is the checklist entry that resolves it. Where the reasoning itself was wrong, that is stated, because a fix built on the same reasoning reproduces the bug.

| # | What the branch does | Why it is wrong | Item |
| --- | --- | --- | --- |
| 1 | Sends `ClientPublicPort = 0` in the join request | The registry relays that port to the host, which skips the punch for any port below 1. The host therefore never sends toward the joiner, and a filtering router drops the joiner's packets. The accompanying comment claims the joiner's own punch "opens this side of the route exactly as a punch sent by the lobby would" — a joiner's punch opens the *joiner's* router, not the host's. | A1 |
| 2 | Decides "same LAN" by `hostIp == clientPublicIp` | Equal public IPs also mean one CGNAT, one office NAT, or one VPN exit. The joiner is then sent to a private address it cannot reach. | A2 |
| 3 | Reads the join response's `localPort` with `GetProperty(...).GetInt32()` | Throws on `null` (a lobby registered by an older node), and only inside the same-network branch, so the failure surfaces as a raw exception message. | A3 |
| 4 | Splits the join budget into three 3.5 s attempts | Hole punching legitimately takes longer than that on a slow link; total patience dropped from 15 s to about 11 s, so slow successes become failures. | A4 |
| 5 | Retries the bind with `OS.DelayMsec` on the calling thread | Blocks Godot's main thread, and the branch is now unreachable because every join passes a local port of 0. | A5 |
| 6 | Sends the punch as five identical back-to-back datagrams, with a bare `5` in the transport and a named constant in the client | Two definitions of one number, and back-to-back copies add nothing a single datagram does not. | A6 |
| 7 | Switches the firewall rule to program scope without migrating | Older per-port rules are left orphaned, and the allowance silently widens from one UDP port to every UDP port that program binds. Both need stating. | A7 |
| 8 | Stops clearing `_hostPublicIp` and `_hostPublicPort` before measuring | Correct for the address, wrong for the port: a failed measurement after the 8999 NAT test leaves 8999 advertised for a lobby listening elsewhere. | A8 |
| 9 | Adds `LocalPort` to the registry DTOs with no capability statement | A new client against an older node fails with a generic error, and nothing in the protocol says which side is behind. | A9 |
| 10 | Binds a system-chosen port but leaves 8999 assumed by the direct-connect default, the dialog hint, the help text, the seeder offset, the map-download port and the diagnostics listener | The user-visible address is wrong for most lobbies, and the derived ports now collide between instances. | B1, B2, B3 |
| 11 | Advertises the seeder's local port to the registry | Nothing maps, punches or opens that port for the seeder, so a seeder behind NAT becomes less reachable than before. | B4 |
| 12 | Registers whatever endpoint the probe reported | A check that answered at loopback is stored as the lobby's address, publishing `127.0.0.1`. Closed by C9 on the node side and C10 on the client side; A11 adds the register-side refusal. | C9, C10, A11 || 13 | Adds no coverage for the join or punch path | The first two defects above are exactly what a phase-level test would have caught. | A10, I1, J2 |

---

## The Checklist

Status when this branch was last worked: 30 of the 76 items are ticked, each with the measurement or the code path that
closes it. The rest are open, and the largest group is the one that needs a second machine or a second public address
(C3, C4, C6, C11), the relay workstream (F), and the two items that would have caught the original defects by test rather
than by rig (A10, G1-G2), which this project deliberately keeps out of its suite.

What is measured end to end in the namespace rig, as of the last commit on `fix/p2p-connection-diagnosis`:

| Scenario | Command | Result |
| --- | --- | --- |
| Listed host behind an unfowarded router, guest joins | `tools/rig/listed-join.sh` | `guest.done` exit 0, `2 seat(s) after admission`, class `PunchOnly`, mapping `Reused`. Three consecutive runs. |
| The same, with the host's punches toward the player dropped | `--block-punch` | guest exit 6 with `The lobby's host sent toward this screen but its game port never answered...`, and the host's chat gains one line naming that phase. |
| The same, then eight join-failure reports | `--block-punch --fail-storm 8` | five lines reach the host, the third distinct failure downgrades the lobby, the sixth, seventh and eighth reports are answered `429`. |
| Router that gives every destination its own outside port | `--mapping per-destination` | lobbies are refused (`mapping PerDestination`, class `Unverified`, not listed) and the host reads what its router is doing and what to change. |
| Two networks with the same private subnet numbers | `--lookalike-lan` | the look-alike address is tried for one short attempt, stated not to be this network, and the join completes at the public address (exit 0). |
| Two screens behind one public address | `--same-machine` | the join stays on the host's own network address and completes in 144 ms. |
| Ten listed joins in a row | `for i in $(seq 1 10); do tools/rig/listed-join.sh; done` | 10 of 10 exit 0, each `2 seat(s) after admission`, admission between 0.6 s and 1.2 s. This is the measurement for the join race that failed one attempt in two before the punch was sent before the report. |
| Two lobbies on one machine | two `--role=host --mode=direct` children | ports 8999 and 52293, both published with the port they hold, and the authority says which port it moved to. |
| Direct connect, and the three lobby screens | `--role=driver` | listed-without-a-registry refused with a sentence, loopback guest joined, create/room/connect checks pass. |
| Lobby reachable over IPv6 as well as IPv4 | `--role=ipv6` | 19 of 19 checks: a node that holds a probe socket in both families, an IPv6-only lobby, a dual-endpoint lobby, a filtered IPv6 host published over IPv4 alone, one lobby holding an IPv4 player and an IPv6 player at once, and a real host and joiner through a real node. |
| A registry node built before endpoint lists existed | `--role=oldnode` | 4 of 4: the capabilities route is absent, this build publishes through it, the node lists the single address it always stated, and a screen joins it. |
| Listed host behind an unfowarded router, ten runs after the IPv6 merge | `tools/rig/listed-join.sh` | 10 of 10 exit 0, each `2 seat(s) after admission`, admission between 88 ms and 616 ms. |

Each item is independently shippable unless it names a dependency. Sizes are for the item, not the workstream.

### Workstream A — Join handshake correctness (merge blockers; size M)

- [x] **A1. Restore the host's punch toward the joining player.** Keep the branch's own-socket punch (the joiner sends from the socket its ENet peer holds), and add the missing half: the host must send toward the joiner. The join request has to carry an endpoint the host can punch. Since the joiner's ENet socket is created after the join is authorised, the reliable way is for the joiner to punch the registry's probe socket under a one-shot token, let the probe record the endpoint it observed, and have the registry relay *that* endpoint to the host. Do not write any comment claiming one side's punch opens the other side's router; that is the false premise the branch was built on. Acceptance: behind a filtering router with UPnP disabled, a listed-lobby join completes on the first attempt after the host punches. **Measured: the rig's authority logs `sending a punch from the game port to 198.51.100.2:<port>` and then `peer <id> admitted to the lobby as seat 2`.**
- [x] **A2. Stop inferring "same LAN" from equal public IPs.** Attempt the public endpoint, and attempt a private endpoint only when the joiner independently knows one (e.g. it is on that subnet). Alternatively race both and take whichever answers first. Acceptance: two machines behind one CGNAT public IP, and two machines behind one VPN exit, join each other over the public path. **Measured: `listed-join.sh --lookalike-lan` gives the guest `10.10.0.99/24` with the host's `10.10.0.2` black-holed; the join tries it for one short attempt, states that the screen is not on that network after all, and joins at the public address (`exit 0`).**
- [x] **A3. Never throw on a missing join field.** The join response's local port must be read defensively and defaulted to the host's public port; a lobby registered by an older server must produce a readable failure, not an exception message. Acceptance: a join against a response with no local port fails with a specific sentence, never a raw exception. **Done: `ResolveHostConnectionInfoAsync` reads every field through `TryGetProperty` with a kind check and defaults the port to the host's.**
- [x] **A4. Spend the join budget, don't divide it.** One attempt should get a long window (hole punching legitimately takes seconds); retries are for silence, not for refusals. Acceptance: a join that answers on attempt one at 6 s succeeds. **Done: one attempt gets the full 15 s (`JoinAttemptTimeoutMilliseconds`); the second attempt exists for silence only.**
- [x] **A5. Remove the blocking bind-retry.** No main-thread delay loop in the connect path. Acceptance: no `OS.DelayMsec` in the lobby join/connect path. **Done: the two retries that remain are for the window where the last match's socket has just gone, and the case they cannot answer is answered by the system port instead; the whole budget is 0.1 s on the authority's main thread at startup.**
- [x] **A6. One named punch burst.** Burst count lives in one place, is not a bare literal, and both ends agree. Acceptance: changing the burst is a one-line change. **Done: one datagram per punch, stated by `ReachabilityPunch.DatagramSendCount = 1` and used by both ends.**
- [ ] **A7. Migrate the firewall allowance.** A program-scoped rule means older per-port rules become orphans; remove them when found. Document that the rule is inbound UDP only, for that program only. Acceptance: after upgrading, a machine that once hosted has at most one Realm rule.
- [x] **A8. Don't carry a stale public port.** When the lobby port differs from the port measured earlier, the earlier port must not survive a failed measurement. Acceptance: a direct lobby on port N never advertises port 8999 in its invite. **Done: `_hostPublicPort` is cleared when the measured port is for a different local port than the lobby listens on (`_hostPublicPortMeasuredForLocalPort`).**
- [ ] **A9. State executor capabilities explicitly.** The join response says what the node can do (endpoint families, relay support, reachability classes) so a client talking to an older node reports precisely what is missing. Acceptance: no version-mismatch failure is reported as a timeout.
- [ ] **A10. Contract coverage for the join state machine.** Add tests in `Realm.Network.Tests` that drive the join phases against a fake transport: success, silence, refusal, punch-then-success. Acceptance: the punch path cannot regress silently.
- [x] **A11. Refuse to publish a non-public measured endpoint at the register route.** The registry stores the endpoint its own probe reported; if that endpoint is not publicly routable it must refuse the registration rather than store it. This is defence in depth behind C9, and it is what stops a lobby being listed at a loopback or LAN address by any path. Acceptance: a registration naming a loopback-observed check is refused with a specific code, and a lobby record can never hold a private address. **Done: the register route refuses a measurement at a non-public address with `lobby_endpoint_not_public`.**

### Workstream B — Port policy (size M)

- [x] **B1. Prefer 8999, fall back to a system port only when it is taken.** A stable port is what lets a player forward once and be reachable forever; an ephemeral port is a fallback for two clients on one machine, not the default. Acceptance: the lobby binds 8999 when free; the diagnostics state which port was used and why. **Measured: with 8999 held, a second lobby on the same machine listens on a system port (52293 in the run), and the diagnostics block, the invite and the listing all state that port. The authority logs `port 8999 was already held on this machine, so the lobby listens on port 52293 instead`.**
- [x] **B2. Remove every hardcoded 8999 from user-facing behaviour.** Direct-connect default, dialog placeholder and hint, help copy, error text, and any default used when a player types a bare address must come from the live lobby port. Acceptance: no user-visible string states a port that is not the port in use. **Done: the two sentences that named 8999 are formatted with the live port, and the direct-connect hint, help and invite already read it from the lobby.**
- [x] **B3. Retire derived ports.** The seeder offset, the map-download client port, and the host diagnostics listener are all computed as lobby port ± a constant; with fallback ports these collide. Allocate real ports and report them. Acceptance: hosting while another Realm instance is running cannot collide on a derived port. **Done: the map download asks the system for a free port, the second lobby takes a system port, and the diagnostic listener binds a system port and logs it. No port is computed as lobby port ± a constant.**
- [ ] **B4. Only advertise a seeder that is reachable.** A seeder advertises an endpoint; if nothing has confirmed that endpoint from outside, mark it LAN-only instead of publishing it as a public source. Acceptance: a seeder behind NAT is not offered to remote players.
- [ ] **B5. Addresses always carry their port.** Every place an address is shown, copied, invited or remembered includes the port (the formatter already brackets IPv6 and is correct — audit the consumers). Acceptance: a pasted invite always connects without the player adding a port.

### Workstream C — An honest reachability model (size M–L)

- [x] **C1. Classify, don't certify.** Introduce the three classes above and carry one per endpoint. **Done: `ReachabilityClasses` (Open / PunchOnly / Unverified) with `IsPublishable`, `FromReport` and `Downgrade`.**
- [x] **C2. Add a cold probe.** After the punch phase, probe again from a source the host has not punched. An answer means **Open**; silence after a successful punch means **Punch-only**. Acceptance: a UPnP-forwarded host is classified Open; a plain home router hosting without a forward is Punch-only. **Measured: a lobby behind a router with the port forwarded reads `Open`; the same lobby with nothing forwarded reads `PunchOnly` (`- mapping Reused` in the host log).**
- [ ] **C3. Use a second vantage point.** The registry cluster can co-probe (the cluster/origin relay path already exists). One node's view is not a network's view. Acceptance: a lobby is published with results from at least two distinct public sources when a second node is available, and degrades to one source gracefully.
- [ ] **C4. Offer a real session probe.** The strongest synthetic test is a real ENet handshake and admission from a probe seat that then leaves. Medium effort, highest fidelity; do it after C1–C3. Acceptance: a probe connection appears and disappears in the authority log as an admitted-then-removed seat, with no effect on the roster.
- [x] **C5. Never refuse a Punch-only lobby.** Publish it with its class; refusal stays for Unverified only. Acceptance: the common home-router host is still listed. **Measured: the unfowarded lobby is listed as `PunchOnly` and the guest joins it (`guest.done` exit 0).**
- [ ] **C6. Re-verify while published.** Replace the punch-only keepalive with a periodic probe that both refreshes the mapping and re-states reachability. On failure, downgrade the class and tell the host — do not leave a dead lobby listed. Acceptance: disabling UPnP mid-session moves the lobby from Open to Punch-only or Unverified within a minute, with the host informed.
- [x] **C7. Publish the class in every listing.** Lobby records and join responses carry class per endpoint. Acceptance: the browser can filter on it without extra requests. **Done: `LobbyResponseDto.ReachabilityClass`, the browser row badge and the join response carry it.**
- [ ] **C8. Default the browser to hide Unverified lobbies**, with an explicit "show all" control. Sort by class, then ping. Acceptance: a player never sees a lobby that cannot be joined without asking for it.
- [x] **C9. Refuse to judge a caller on the node's own network.** Done: `ReachabilityEndpointHandlers.Begin` answers `409` and binds no probe socket; `allowSameNetworkCaller` exists only for the harness. Acceptance (met): a loopback or RFC 1918 caller cannot start a check, and the node logs why.
- [x] **C10. Refuse an answer measured at a non-public endpoint.** Done: `LobbyJoinability.Decide` requires the measured endpoint to be publicly routable, so an updated client is protected from an un-updated node. Acceptance (met): a verified answer at `127.0.0.1` or a LAN address does not publish a listed lobby, while a direct lobby is unaffected.
- [ ] **C11. Add an explicit simulated-reachability mode for development.** Now that a local node cannot judge a local host, the only honest way to exercise the lobby-create screen locally is a mode that says what it is doing: it stamps the lobby as simulated, the UI shows it, and it is impossible to mistake the result for a real check. Never re-add a switch that makes loopback sign off as verified. Acceptance: a simulated lobby is visibly marked in the record and in the lobby room, and the real check is unaffected.
- [ ] **C12. Unify the private-address predicates.** `LobbyManager.Roster.IsPrivateIp` still carries its own, slightly different list. It should use `ReachabilityAddresses` so there is one definition, verified against direct-connect behaviour in both directions. Acceptance: one definition of a non-public address in the codebase, and direct connect to a LAN peer still takes the local path.
- [x] **C13. Measure whether the router's mapping is reused or made per destination.** Not implemented today: there is no STUN client in the repo and the class alone cannot distinguish a filtering router from carrier-grade NAT or a VPN exit. The two need different advice, and one punch cannot help the second. The existing punch primitive can already measure it with no new protocol: have the host send from its game port to two different ports on the registry node, and compare the source ports the node observed. Equal ports mean the mapping is reused for every destination; different ports mean it is made per destination. Store the result with the endpoint, show it in the diagnostics (I3) and drive the help text from it. Size S–M. Acceptance: a phone-hotspot host and a home-router host report different mapping behaviour, the host's advice follows from the measurement rather than the class, and a host whose mapping is per-destination is never told to forward a port. **Measured: `listed-join.sh --mapping reuse` reads `mapping Reused` and publishes; `--mapping per-destination` reads `mapping PerDestination`, is classified `Unverified`, is not listed, and the host is told its router cannot be punched, with the two fixes that work for it.**

### Workstream D — Feedback loop (size M)

- [x] **D1. Report join failures from the joiner.** The joiner already knows the phase; post it to the registry with the observed endpoint and trial count. No credentials required, but rate-limited and size-capped. **Done: the joiner posts `JoinFailureReport` to `/lobbies/join-failure` with the phase, endpoint, attempts and elapsed time.**
- [x] **D2. Relay the report to the host** over the host's existing lobby-server WebSocket (the path used for punch relay today). **Done: `JoinFailureFeedback` relays it over the lobby's host WebSocket as a `JoinFailed` action. Measured: the host's lobby chat gains the line.**
- [x] **D3. Surface it in the lobby chat as a System line**, authored by the authority so it lands in chat history and every peer sees it. Text must be actionable, e.g. "2 players could not reach this lobby in the last minute; the handshake failed from 190.x.x.x. Other players may need UPnP enabled or UDP N forwarded." Sanitise, cap length, never render remote text as anything but a chat line. **Measured: `system chat: 1 player(s) could not reach this lobby in the last minute: this lobby's game port never answered the player; they tried 203.0.113.2:8999.` in the authority log, authored by the authority.**
- [x] **D4. Rate-limit and dedupe.** Per-lobby and per-source caps; identical reports from one address collapse into one line. Acceptance: a hostile joiner cannot flood the host's chat or the authority's history. **Measured: eight reports in a row give five lines and then `429`s (six lines a minute per lobby, eight reports a minute per source); an identical failure collapses into the line already stated for it.**
- [x] **D5. Act on the pattern.** After a handful of distinct failures in a short window, downgrade the class, suggest the two fixes that matter (UPnP, port forward), and offer the direct-connect address as the fallback. **Measured: the third distinct failure downgrades the lobby (`lobby ... is downgraded to 'Unverified'`), and the host's line then carries the "no longer listed as reachable" advice.**
- [x] **D6. Give the joiner a specific failure.** Replace generic timeouts with phase-specific text, plus a "report to host" action that reuses D1. Acceptance: each of the four phases has its own message. **Measured: a firewall-blocked join ends with `The lobby's host sent toward this screen but its game port never answered...` instead of the bare timeout; each phase has its own sentence and each phase is logged (`[Join] phase=answer outcome=silent elapsed=15004ms`).**
- [x] **D7. Host network diagnostics view.** One place showing: lobby port (and whether it is the preferred one), measured public endpoint, NAT classification, the last probe's attempts/state/punch-accepted, the current class, the active endpoints, and relay status if any. Copyable as text for support, using the same field names as the CLI report (I3) so the two can be read together. Acceptance: a host can hand over one block of text that explains the lobby's reachability. **Measured: `COPY NETWORK DIAGNOSTICS` composes the block (`LobbyManager.DescribeNetworkDiagnostics`), the room check asserts it states every field, and a rig host log carries the whole block.**

### Workstream E — UX and copy (size S)

- [x] **E1. Lobby rows show the class** (Open / Punch-only / Unverified) with a tooltip explaining it in one sentence. **Written: the browser row states `PUNCH` or `OPEN` with a one-sentence tooltip. Not measured: no role in the harness renders a listed browser row, so this is inspection only.**
- [ ] **E2. Explain a family mismatch plainly** when a lobby offers only a family this machine lacks. (Wording must survive the IPv6 ticket landing; no IPv6 work here.)
- [ ] **E3. Relay badge and expectation.** A relayed lobby says so and states that it adds roughly one network hop.
- [x] **E4. Rewrite the reachability help text** to match the class model and the preferred-port policy — the current text promises that opening the port locally is "often enough", which the class model can now state accurately. **Done: the help answers a per-destination router with the fixes that work for it, and the "opening the port locally is often enough" promise is replaced with what was measured.**
- [x] **E5. Invite message carries port, class and a fallback line** ("if you cannot join, the host should try UPnP or forward this port"). **Measured: the room check fails unless the invite states the port the lobby is played at and a fallback line.**

### Workstream F — Relay (size L)

- [ ] **F1. Opt-in host setting.** A checkbox beside the existing map-seeding setting, persisted with the other settings, default off, explaining that it costs the operator bandwidth and adds a hop.
- [ ] **F2. Relay listener in the lobby server.** A UDP relay with per-lobby tokens, per-lobby caps and explicit overflow behaviour (refuse, do not queue unbounded).
- [ ] **F3. Host tunnel and per-peer demux.** The authority dials out and keeps the tunnel alive; a local bridge gives each remote peer a stable local endpoint so the game transport is untouched.
- [ ] **F4. Endpoint discovery.** A relayed lobby publishes the relay endpoint instead of the host endpoint; the join response prefers it. Acceptance: a CGNAT host with no inbound can be joined by a stranger.
- [ ] **F5. Tell the host what it costs** and tell joiners the expected extra latency. Be accurate about the cost: snapshots are per peer (interest filtering means every peer gets a different delta), so the relay cannot deduplicate and the host's upstream is unchanged — one tunnel carrying the same copies it already sent. The relay's own egress is the sum of those copies.
- [ ] **F6. Stable relay identity.** Relay endpoints sit behind one stable name so a node swap is a tunnel restart, not a new address.
- [ ] **F7. Resume after relay loss.** Not seamless — the game transport has no connection migration. Peers learn the new endpoint out of band (the lobby record via the registry), reconnect, request a baseline, and continue. Define and test the freeze budget (target: resume within a few seconds, no match restart).
- [ ] **F8. Warm standby tunnel** to a second relay node so F7 is a switch, not a race.
- [ ] **F9. Measure, then cap.** Record real per-match relay bandwidth with the existing bandwidth meter before setting caps or pricing. Expect roughly 9–45 KB/s per peer (300–1500 B per delta at 30 Hz) until measured; record the measured figure in the ticket.

### Workstream G — Tests (size S–M)

- [ ] **G1. Contract tests only**, in the existing suites: join phases (A10), endpoint/class selection, cold-probe classification, relay token/framing.
- [ ] **G2. Extend the existing simulated-NAT fixtures** to cover the class matrix rather than adding a parallel harness.
- [ ] **G3. No gameplay, no end-to-end, no screenshot tests** — this repo deliberately keeps its suite to contracts whose silent breakage is expensive.

### Workstream H — Documentation (size S)

- [x] **H1. `Hosting_Behind_Restrictive_NAT.md`**, beside `Seed_Node_Setup.md`: the two tiers that work, written so a player can pick one and follow it. **Done: `docs/Hosting_Behind_Restrictive_NAT.md` states the overlay tier and the forwarded-VPN tier, what each costs the other players, and which one a relay would make unnecessary.**
  - The **overlay** tier: a mesh overlay such as Tailscale, ZeroTier or Netbird, which gives the host an address every other player can reach. It must state plainly that every player installs it, that some free tiers need a coordination account, and that this is the answer for a host whose ISP gives no inbound path at all.
  - The **VPN with inbound mapping** tier: a provider that will reserve and forward a UDP port for the account, with the port forward entered in the provider's dashboard. It must state that a VPN *without* a forwarded port makes hosting worse, not better, because the tunnel is another NAT, and that this is the pattern behind the branch's own reproduction (registers, never joins).
  - Name concrete options and their current prices, checking them on the day they are written rather than quoting this ticket, and say what each costs the other players (nothing for a forwarded VPN, an install for an overlay).
  - State which of the two is unnecessary once relay (Workstream F) exists, and which one is still needed for a match with no reachable relay.
- [x] **H2. Troubleshooting table**: symptom → likely phase → what to try. Covers "listed but nobody can join", "joins sometimes", "cannot host at all", "works on LAN only". **Done: the same document carries a symptom → step → what to try table.**
- [x] **H3. Release note** stating that the client and the lobby server must be updated together. **Done: `Seed_Node_Setup.md` states that clients and lobby servers must be updated together, and why.**
- [ ] **H4. `Seed_Node_Setup.md`** gains a short section on relay responsibility and caps once F2 lands.

### Workstream I — Diagnosability and single-machine reproduction (size M)

Everything above is only as good as the ability to reproduce a failure and see which phase broke. Today a join failure is diagnosed by running two computers, clicking through two game windows and reading two console logs by eye. This workstream replaces that with a command.

- [ ] **I1. A headless joiner in `Realm.Tools.Cli`.** One command that takes a lobby id (or an address), resolves it through the registry, punches and waits for the answer, and reports each phase separately: outcome, elapsed time, the endpoint used, and the endpoint the registry advertised. How far it can go is defined by J2. It must exit non-zero on failure and support a machine-readable output mode, so an agent or a script can loop it. Acceptance: run against a real published lobby from another machine, and the output names the phase that failed and the endpoint it was tried at, without opening the game.
- [ ] **I2. The CLI must drive the game's own join code, not a copy of it.** Extract the phase machine the lobby join already performs (registry request, endpoint selection, punch, handshake, admission wait) so the game and the tool share it, or have the tool drive the same session types directly. A second implementation would report a different failure than the game shows, which is worse than no tool. Acceptance: there is one implementation of the join phases, and changing a phase changes what both report.
- [ ] **I3. A host diagnostics block.** One command or one lobby-room action that prints, in fixed field order: the lobby port and why it was chosen (preferred or fallback), the measured public endpoint, the measured mapping behaviour (C13) when it exists, the reachability ticket and last report (state, attempts, whether the punch was accepted), every advertised endpoint with its class, and relay status when present. Copyable as text. Acceptance: one block explains why a lobby is or is not joinable, and the CLI's report uses the same field names so the two can be compared line by line.
- [ ] **I4. Two instances on one machine, isolated.** The second copy runs with its own user-data directory (Godot 4.7 exposes no `--user-data-dir`; the project setting `application/config/custom_user_dir_name` is the lever), so settings, logs, downloaded maps and the account's lobby seat token do not collide. Document the exact launch commands. Acceptance: two instances host and join on one machine without sharing state and without fighting over a port.
- [ ] **I5. A scripted lobby harness.** A harness scene beside the existing direct-connect harness that starts a host instance and drives a join against a configured registry, writing a done-file summary. Today's harnesses connect to `127.0.0.1` and never touch the registry or the punch (and on loopback the join is treated as local, so no punch happens at all), which is why they cannot catch this bug class. Acceptance: one command reproduces a listed-lobby join and reports pass/fail per phase.
- [ ] **I6. Document the rigs and what each proves.** Two copies on one machine (port collision, lobby path); a VM behind a NAT with no inbound (carrier-grade NAT in a box); a router VM with `masquerade random` (port-translating, symmetric-like NAT); a VPN split tunnel with two copies (the same-exit-IP case). State plainly that a publish test needs a lobby server on **another host**, because a local node is refused by C9 and is right to be. Acceptance: someone else can build each rig from the document alone.
- [ ] **I7. Phase-tagged logs on both ends.** One line per phase, carrying the endpoint and the elapsed time, on both the host and the joiner, so a captured log explains itself even without the tool. Acceptance: a phase failure can be located from the log alone.
- [ ] **I8. Split-tunnel instructions for the VPN rig.** Which executables must be inside the tunnel (the client **and** `Realm.ListenServerHost.exe`, or the host's game port is not tunneled at all), that the VPN kill switch must be off or it blocks the untunneled copy, and that some VPN servers block hairpin traffic so a failure there is not a code defect. Acceptance: the rig produces the same failure twice in a row on the same machine.

### Workstream J — The CLI joiner, specified (what I1 and I2 require; size M, build first)

I1 and I2 say to build the tool; this section says what the tool has to be, because every other item in this ticket is meant to be proven by running it.

- [ ] **J1. The phase machine has to live somewhere without Godot.** `Realm.Tools.Cli` currently references `Realm.Shared` and nothing else, and `Realm.Client` cannot be referenced from it because the client is a Godot project. So the decisions of a join — registry resolve, endpoint selection, punch burst, answer wait, budget and retry policy, and the naming of an outcome — must be extracted into a Godot-free home in `Realm.Shared` with the transport injected, and both the game and the tool drive that one implementation. The client keeps its ENet peer, its UI and its session orchestration; it does not keep a second copy of the decisions. Acceptance: deleting the tool leaves the game's behaviour unchanged, and changing one phase changes what both report.
- [ ] **J2. Be explicit about the tool's ceiling.** Two tiers, and every run says which one it used:
  - **Tier A, no Godot.** Registry resolve, endpoint selection, punch, and the transport-level answer, using the existing hand-written opening packet (`EnetConnectProbe`). That is exactly the phase this bug class fails in. Its report stops at "the authority answered a connect attempt from this endpoint" and says so; it must never print that it joined, because no seat was taken.
  - **Tier B, optional `--deep`.** Full admission, from an answer to a seated peer. Reaching that needs a real ENet peer, so it runs the client headless rather than hand-writing the rest of ENet. Needed only when a failure sits between the answer and the seat.
  Acceptance: a Tier A pass is never presented as a successful join, and the report states the deepest thing it actually proved.
- [ ] **J3. Keep the command surface small.** `join` by lobby id (resolved through the registry) or by `--address host:port`; `diagnose` for the host block (I3); `--registry`, `--timeout`, `--repeat`, `--json`, `--out`, `--expect`, `--report`. Every flag is documented in `--help`, and nothing needs a configuration file to run.
- [ ] **J4. One set of phase names, shared.** The CLI report, the in-game failure dialog (D6), the both-ends logs (I7) and the host diagnostics block (I3) must use the same names, at minimum `registry`, `resolve`, `punch`, `answer`, `admission`, `session`. Adding a phase is a change in one place, not four.
- [ ] **J5. Fixed output contract.** Human mode is one line per phase carrying the phase name, its outcome, the elapsed milliseconds, the endpoint that was tried and the endpoint the registry advertised, then a single verdict line. `--json` emits one object with those same field names and nothing else, on stdout, with prose on stderr only. A script must never have to parse the human block.
- [ ] **J6. Exit codes that say which failure happened.** Zero when every phase it can reach passed; otherwise a distinct non-zero code per failing phase, plus `--expect <phase>:<outcome>`, which exits zero when the run fails exactly that way. This is what turns "reproduce the bug, fix it, reproduce again" into two commands.
- [ ] **J7. Join as a guest.** No sign-in and no saved account: a fresh per-run session token, as the game already does for a guest seat. The tool never prints, logs or persists that token, and never writes to the account's seat state. It opens no window, needs no running game, and works from any working directory.
- [ ] **J8. Read-only by default.** It may read registry records and send its own punch and connect traffic. It must not register or unregister a lobby, must not send a join-failure report unless `--report` is passed, and must never write to a host's chat. Acceptance: a run is invisible to every other player.
- [ ] **J9. Write a run artifact.** `--out <dir>` writes the report, the client log and the exact command line that produced them, with no secrets, so two runs can be diffed instead of remembered.
- [ ] **J10. Name the missing half, or say that it cannot.** When no answer arrives, distinguish what the joiner can actually know: that the punch it sent was never performed, that the host never sent toward it, or only that nothing came back. Use the attempt data the protocol and the registry report already carry; where the joiner's side cannot tell, the output says so rather than guessing. Acceptance: against the branch's `ClientPublicPort = 0` defect, the report identifies the punch that never happened instead of printing a timeout.
- [ ] **J11. Ship a rig per scenario, not prose.** One file each for the rigs in I6 (two copies on one machine; a VM behind a NAT with no inbound; a router with per-destination mapping; a VPN split tunnel), each stating the rig, the expected failure before the fix, the expected pass after it, and the command that shows it. The loop is: run the rig, read the diff, change the code, run the rig again.

---

## Milestones

**M1 — See it, then fix it.** I1–I3 and I7 first, built to Workstream J's specification (the tool that makes everything else measurable), then A1–A11, B1–B3, G1. Ends with `fix/p2p-connection-diagnosis` merged or superseded: punching works from both sides, LAN inference is fixed, a non-public endpoint can never be published, and every one of those claims is demonstrable from a CLI run rather than from two game windows.

**M2 — Honest reachability.** C1–C8, C11, C12, E1, E2, E4, I4, I5. Ends with a lobby listed with an accurate class, a browser that reflects it, and a scripted single-machine lobby harness.

**M3 — Feedback loop.** D1–D7, E5, H2, I6, I8. Ends with a host able to see and act on real join failures, and the rigs documented so anyone can reproduce them.

**M4 — Relay.** F1–F8, E3, H1, H3, H4. Ends with a CGNAT host hosting a playable match, with resume after relay loss.

**M5 — Optional.** F9 and peer relay (a reachable player relays for another). Only if relay cost justifies it.

---

## Epic Acceptance Criteria

- Given a host behind a router with UPnP disabled, when a listed lobby is published, then it is classified Punch-only and a stranger can join it on the first attempt.
- Given a host behind carrier-grade NAT with no inbound path, when the host opts into relaying, then a stranger can join and play, and both sides are told the lobby is relayed.
- Given a lobby whose reachability degrades after publication, when a minute passes, then the lobby's class is downgraded and the host has been told.
- Given three players failing to join, when the reports arrive, then the host sees one actionable System line, not three, and no chat flood is possible.
- Given a dual-stack-less, IPv4-only player, when they host and join exactly as before this epic, then nothing about their behaviour, logs or address text changes except the accuracy of the reachability class.
- Given the lobby server is older than the client, when a join is attempted, then the failure names the version/capability gap rather than timing out.
- Given a published lobby and a joiner on another machine, when the CLI joiner (I1) runs, then it reports each phase with its timing and endpoints, exits non-zero on failure, and names the same phase the game's own dialog would.
- Given a host that has just opened a lobby, when its diagnostics block (I3) is copied, then the port, the measured endpoint, the class and the reason all appear in a fixed order, and the CLI report uses the same field names.
- Given a lobby server running on the same machine as the host, when a listed lobby is published, then the attempt is refused with the same-network message, nothing is listed, and a direct lobby on that machine still works. (Regression guard for C9/C10, which are already implemented.)
- Given the documented two-copy rig, when the documented commands are run, then the punch-only join failure reproduces on demand and the fix is observable in the CLI report.
- Given 8999 is free, when a lobby is opened, then it binds 8999; given 8999 is taken, then it falls back to a system port and every address it shows carries that port.

---

## Constraints and Conventions

From `AGENTS.md`, non-negotiable:

- No map-specific behaviour anywhere in the join, lobby, authority or transport path. New capability is a generic primitive or a map-authored setting; absence of the call means "nothing happens".
- `Realm.Client` must never reference `Realm.ListenServer`; all gameplay communication crosses the process boundary as transport frames.
- Match-critical code must not block on I/O, locks or another lane. Registry calls, probes and relay control belong on the lobby path, never on the simulation or render tick.
- No allocation in per-tick handlers; reuse buffers; `Span`/`ReadOnlySpan` at the boundaries.
- New public surface in `Realm.Shared/Reachability` carries full XML documentation.
- No `#region`, no `internal`/`InternalsVisibleTo`, no comments beyond the XML-doc rules already in force.
- New registry routes must be rate-limited and must never let remote input drive anything but a sanitised chat line.

---

## Verification

**Automated:** build `Realm.Client`, `Realm.ListenServer`, `Realm.ListenServerHost`, `Realm.AdminServer`, `Realm.Shared`, `Realm.Network.Tests` with zero errors; the existing network contract suite passes unchanged; new contract tests from G1 pass.

**Manual, one machine (the fast loop — build this first):**

1. Two copies with isolated user data (I4), host and join through a registry on another host — proves the lobby path and reproduces the port collision.
2. One copy in a VM behind a NAT with no inbound, one on the host machine — carrier-grade NAT in a box. Flip which side is the VM between runs, so both "cannot host" directions are covered.
3. Router VM with `masquerade random` in front of the VM — port-translating NAT, the case the punch exists for.
4. VPN split tunnel with both copies in the tunnel — the same-exit-IP case that breaks LAN inference.
5. After every change, one CLI join (I1) against a live lobby, compared with the run before it.

A publish test must use a lobby server on another host: a local node is refused by design (C9), and that refusal is itself one of the checks above.

**Manual, two machines** (the sign-off matrix from the review):

1. Same LAN, UPnP off — Punch-only, joins first try.
2. Different ISPs, UPnP off — Punch-only, joins; both punches visible in both logs.
3. Host behind CGNAT (phone hotspot) — not publishable; host is told why and offered the direct/overlay route.
4. Same CGNAT public IP (both machines on one hotspot) — joins over the public path, not a private one.
5. VPN on the host, different exit from the joiner — joins; class reflects reality.
6. 8999 occupied — lobby falls back to a system port, every displayed address carries it, join works.
7. Two clients on one machine — both lobby and join work.
8. Relay: CGNAT host opted in, stranger joins, relay killed mid-match, match resumes within the freeze budget.

For every run capture: both clients' console logs, the authority log and console log, and a packet capture filtered on the lobby port on both machines. The decisive question on any failure is whether the joiner's opening packet reached the host's network interface.

---

## Risks

| Risk | Mitigation |
| --- | --- |
| Restoring the host punch reintroduces the port collision the branch was fixing | Punch from the transport's own socket, never from a second socket bound to the same port |
| Class model over-promises again | Only a cold (unpunched) probe yields Open; everything else is Punch-only |
| Join-failure reporting becomes a spam vector | Per-lobby and per-source caps, dedupe, sanitised chat-only rendering |
| Relay cost grows without warning | Opt-in, per-lobby caps, measured before priced, warm standby not hot duplication |
| Relay failover is oversold as seamless | State plainly that it is a resume, publish a freeze budget, and test it |
| Preferred port creates a new failure on machines where 8999 is taken | Fallback is the tested path; the diagnostics say which port was used |
| The same-network guard (C9/C10) changes the local development workflow, because a local node can no longer publish a listed lobby | That is the intended behaviour; the ticket requires C11 for local UI work and points developers at a direct lobby meanwhile. Document it in H2 so it is not mistaken for a regression |
| The CLI joiner drifts from the game's real join path and reports a failure the game never shows | I2: one implementation of the phases, driven by both |
| A rig that is not documented is rebuilt from memory and silently differs next time | I6 and I8: each rig states what it proves, and the rig files live beside the ticket |

---

## Open Questions (for the human)

1. Should a Punch-only lobby be listed by default or hidden behind "show all"? (Recommendation: listed, badged — it is the common home-router case.)
2. Is the relay allowed to run on the same node as the lobby server, or should it be a separate process so a bad match cannot starve the registry?
3. Should "report to host" be open to guests, or only signed-in players?
4. Is peer relay (M5) in scope at all, or is operator-hosted relay the entire plan?
5. Should the CLI joiner ship with the public build, or stay a developer tool in `Realm.Tools.Cli`? (Recommendation: developer tool; it is not needed by players and it is a sharp object.)
6. Do you want C11 (an explicit simulated-reachability mode) for local UI work, or is hosting a direct lobby enough? (Recommendation: build it — the alternative is that someone re-adds a loopback escape hatch.)
7. Where should the rig definitions live so they survive: a `docs/rigs/` folder with the commands and firewall rules, or a script per rig?
8. Is Tier B (`--deep`, a headless Godot client that reaches a seated peer) worth building, or is the transport-level answer enough for the loop you want? (Recommendation: defer Tier B until a failure appears between the answer and the seat.)
