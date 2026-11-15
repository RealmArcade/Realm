# IPv6 Lobby Hosting and Joining — Phase 0 Decision and Delivery Record

This records the decisions the IPv6 ticket asked for before implementation started, the evidence behind
them, and what was actually shipped.

## 1. How IPv6 game traffic is delivered

**Decision: keep Godot's `ENetMultiplayerPeer` on both ends. No transport is replaced, and there is no
second transport for IPv6 lobbies.**

The ticket's Phase 0 gate existed to establish whether the game connection can speak IPv6 at all, because
that answer decides whether the work is two phases or a transport rewrite. It can.

Evidence, measured on this checkout (Godot 4.7 stable mono, Windows, ENet as Godot ships it):

- `ENetMultiplayerPeer.CreateServer(port, peers)` binds **both** families. A process that created a server
  on UDP 8999 held `0.0.0.0:8999` and `[::]:8999` at the same time, which is why the rig's registry node
  reached that port from its own IPv6 socket.
- One such server accepts an IPv4 client and an IPv6 client **simultaneously**. Same peer list, same RPC
  framing, same everything: only the address a peer arrived from differs.
- `CreateClient("::1", port)` connects, and a raw `ENetConnection.socket_send` to an IPv6 literal is
  delivered.
- End to end in the rig: a joiner dialled `[::1]:8999`, the authority seated it as peer 2, and the lobby
  described the seat and the match state over that connection.

So `FrameDelivery.Reliable`, `FrameDelivery.UnreliableOrdered`, `SnapshotFragment`'s per-description
fragmentation, the repair ring and the baseline fallback are all untouched. The ticket's main risk
("Godot's ENet cannot do IPv6, and replacing the transport is far larger than expected") did not
materialise, and the fallback it named — going straight to the relay ticket — would have solved a
different, larger problem than the one the players in the story have.

What the same evidence did *not* show is that ENet needs any IPv6-specific handling that the game must do
itself: the transport has no notion of address family, so the family only ever exists in the control
plane, in what the host advertises and what the joiner dials. That is why the whole change below lives in
`Realm.Shared/Reachability`, `Realm.AdminServer` and the lobby path of `Realm.Client`, and why
`Realm.ListenServer` has no IPv6 code in it at all.

**Estimated slices, as the gate asked for:** the transport slice turned out to be zero. The work that
remained was (1) an endpoint list with a family per endpoint through the registry, (2) a probe service and
verifier that work in the family being probed, (3) endpoint discovery and selection on the client, and
(4) lobby UX that states a family mismatch instead of timing out.

## 2. How the registry carries multiple endpoints

**Decision: a list of endpoints, each with its family, address, port, verification result and probe id.
The single `HostIP`/`HostPort` fields stay, and they carry the IPv4 endpoint, because a node that predates
this change can still be understood by the clients and by the seeder.**

`LobbyEndpointRegistration` / `LobbyEndpointInfo` are the shape. Registration sends every endpoint the
host discovered; the node probes each one **from the socket of that endpoint's own family** and stores the
result per endpoint. Listing and join responses carry the list. The join path then selects by family with
`LobbyEndpointSelection.Choose`, which is also what the registry node runs when it decides whether a punch
is needed for the endpoint it is about to hand out — so a dial and the punch that goes with it cannot
disagree about which endpoint the join is for.

## 3. Protocol evolution against an older node

**Decision: explicit capabilities, not a version number.**

There is a `GET /lobbies/reachability/capabilities` route. A node that answers it reports
`RegistryCapabilities` (currently v2: endpoint lists, and a per-family check capability list). A node that
does not answer it is read as v1 — IPv4 only — and the client keeps exactly the single-endpoint
registration and join behaviour it had before this change. That is the whole compatibility story: an old
node is not guessed at, it is *detected by its silence* and treated as what it is.

A client that asks a node to check a family the node cannot check is refused with
`ReachabilityRefusalCodes.FamilyNotChecked` rather than being told the endpoint is unreachable, because
"this server cannot see IPv6" and "your IPv6 does not work" are different sentences for the player and
different bug reports for us.

## 4. Whether a v6-only lobby may be published

**Decision: only when the v6 endpoint has been measured by the node, and only to joiners who were told
about the family.**

A lobby is published with the endpoints that verified, and nothing else. An IPv6-only host therefore
publishes an IPv6-only lobby, which the listing states per endpoint. A joiner that shares no family with a
listed lobby is refused on the first attempt — measured at 109–190 ms in the rig, against a retry budget
measured in seconds — with `This lobby is reachable over IPv6 only, and this computer has no address in
that family. Mixing IPv4 and IPv6 players in one lobby is not supported yet.` The lobby browser shades the
family a row offers, so a v6-only lobby is legible before it is pressed.

Mixed-family play remains a non-goal and the message says so, rather than failing silently or leaving the
player to infer it from a timeout.

## 5. What shipped

Controls plane (`Realm.Shared/Reachability`):

- `LobbyAddresses` — family, scope and advertisability rules, and the `[address]:port` form every screen
  writes. Link-local, multicast, unspecified and `169.254/0` are never advertisable.
- `LobbyEndpoint`, `LobbyEndpointOffer`, `LobbyEndpointPlan` — the endpoint list, and the offer policy
  (one endpoint per family, global IPv6 first, no router mapping when there is no IPv4 endpoint at all).
- `LocalEndpointDiscovery` — global IPv6, then local IPv4, then public IPv4, then ULA; link-local never.
- `LobbyEndpointSelection` — the one rule both the joiner and the registry node run.
- `UdpReachabilityProbeService` — one socket per family, a probe only ever leaves from the socket of the
  family the endpoint lives in, and `ENetConnectProbe`'s wire format is unchanged because it was always
  plain bytes.
- `HostReachabilityVerifier` — resolves its own probe address in the family it is probing and reports
  `ProbeFamilyUnavailable` when the node cannot answer in that family at all.

Registry node (`Realm.AdminServer`): the capabilities route; a register request that carries a list;
per-family verification with the verified result stored per endpoint; `MapToIPv4()` removed from the
request source so a v6 request is not mangled; listing and join responses that carry the list; a punch
relayed only when the chosen endpoint is one that needs one; `GeoIpService` treating `::1` as loopback;
`ReachabilityProbePublicIpV6` in configuration.

Client: discovery, publication, measurement and selection as above; an IPv6-only host skips STUN, UPnP
and port mapping entirely and is never told its router failed at something nothing asked it to do; the
browser and the lobby room state which families are offered; direct connect accepts, remembers and
redisplays `[addr]:port`; and every IPv4-only log line and sentence is unchanged, byte for byte, so a
machine without IPv6 reads the same log it read before this existed.

## 6. Verification

`Realm.Network.Tests` gained the contract tests the ticket asked for (18 of them) and the existing suite
is unchanged: 46/46 pass. `Realm.Ecs.Tests`, `Realm.Simulation.Tests` and `Realm.Distribution.Tests` pass
except one `ContentAddressableStorage` merge test that fails identically with these changes stashed.

The rig the ticket asked for on real sockets is `Realm.Client/Testing/LobbyDirectConnectHarness` (roles
`ipv6`, `listedguest`, `v6guest`). It starts a real registry node on `http://[::1]:<port>` with both probe
sockets and checks, end to end:

1. the node answers over IPv6 and states it can check both families;
2. the authority's game port is bound in both families;
3. the node reached that port **over IPv6** from its own IPv6 socket;
4. an IPv6-only lobby publishes and lists with one verified IPv6 endpoint and no IPv4 one;
5. a screen with no shared family is refused in ~100–200 ms naming IPv6, not after the retry budget;
6. a joiner connects over IPv6 by address, is seated, and the address is remembered as `[::1]:8999`;
7. a real host publishes a listed lobby through the node over the endpoint list and a real joiner joins
   it over IPv4, which is the check that the endpoint list did not change the IPv4 path.

The two-machine manual pass the ticket lists still has to happen on real networks (dual-stack LAN,
dual-stack across networks, v6-only host with IPv4 disabled, and the two mismatch directions); loopback
can prove the code paths but not a carrier's filtering.
