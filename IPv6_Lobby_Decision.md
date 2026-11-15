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
- `LocalEndpointDiscovery` — global IPv6, then local IPv4, then public IPv4, then ULA; link-local never, and a
  temporary (privacy) address last of all, because one that expires takes the lobby's address with it.
- `LobbyEndpointSelection` — the one rule both the joiner and the registry node run.
- `UdpReachabilityProbeService` — one socket per family, a probe only ever leaves from the socket of the
  family the endpoint lives in, and `ENetConnectProbe`'s wire format is unchanged because it was always
  plain bytes.
- `HostReachabilityVerifier` — resolves its own probe address in the family it is probing and reports
  `ProbeFamilyUnavailable` when the node cannot answer in that family at all.
- `LobbyPortCheck` — asks a game port whether anything answers in a family, using the opening packet a
  registry node's check sends, so a host can ask its own loopback over IPv6 and find out that its transport
  still holds that family.
- `LobbyJoinAnswers` — reads a join answer (the endpoint list, an older node's single address, or something
  unreadable) in one place, because that shape is a promise to every lobby server in the field.

Registry node (`Realm.AdminServer`): the capabilities route; a register request that carries a list;
per-family verification with the verified result stored per endpoint; `MapToIPv4()` removed from the
request source so a v6 request is not mangled; listing and join responses that carry the list; a punch
relayed only when the chosen endpoint is one that needs one; `GeoIpService` treating `::1` as loopback;
`ReachabilityProbePublicIpV6` in configuration.

Client: discovery, publication, measurement and selection as above; a join the server refused states the
server's own reason; an IPv6-only host skips STUN, UPnP
and port mapping entirely and is never told its router failed at something nothing asked it to do; the
browser and the lobby room state which families are offered; direct connect accepts, remembers and
redisplays `[addr]:port`; and every IPv4-only log line and sentence is unchanged, byte for byte, so a
machine without IPv6 reads the same log it read before this existed.

## 6. Verification

`Realm.Network.Tests` holds the contract tests the ticket asked for, plus the ones added after the first pass,
and the rest of the suite is unchanged: **51/51 pass**. `Realm.Ecs.Tests` 14/14, `Realm.Simulation.Tests` 5/5,
and `Realm.Distribution.Tests` 17/18 with the one `ContentAddressableStorage` merge failure that fails
identically with these changes stashed. `dotnet build Realm.slnx` is clean.

### The IPv6 rig — `Realm.Client/Testing/LobbyDirectConnectHarness`, role `ipv6`

Starts a real registry node on `http://[::1]:<port>` with both probe sockets, and reports 19 checks. Three
consecutive full runs, all 19 green:

1. the node answers over IPv6 and states it can check both families;
2. the host's authority holds the game port in **both** families;
3. the host's own production port check (`LobbyPortCheck`) answers over IPv4 **and** IPv6 against the real
   ENet server, which is the check a host runs before it offers an IPv6 address;
4. the node reached that port **over IPv6** from its own IPv6 socket;
5. an IPv6-only lobby publishes and lists with one verified IPv6 endpoint and no IPv4 one;
6. the same port published at both addresses lists **both** verified endpoints;
7. a lobby whose IPv6 endpoint never answered is published over **IPv4 alone**, with no IPv6 endpoint listed
   — a filtered IPv6 host is not silently listed over the family that does not work;
8. a lobby registered **without** an endpoint list (what an older screen sends) is stored as the single
   shared-IPv4 endpoint those screens have always stated;
9. a join to a lobby whose host channel is closed is refused with a reason a screen can show;
10. a join request that states **no** address family is answered with the lobby's IPv6 endpoint, not a refusal;
11. a screen sharing no family is refused in ~110 ms naming IPv6, not after the retry budget;
12. a joiner connects over IPv6 by address, is seated, and the address is remembered as `[::1]:8999`;
13. one lobby holds an IPv4 player and an IPv6 player **at the same time** (3 seats);
14. a real host publishes a listed lobby through the node over the endpoint list and a real joiner joins it
    over IPv4.

### The old-node rig — role `oldnode`

Builds a registry node from `10d18a7a` (the commit before this work, so a node that has never heard of
endpoint lists) and drives it: the capabilities route is absent (404), this build publishes a lobby through it,
the node lists that lobby with no endpoint list and answers a join with the single address it always stated,
and a real screen joins through it. That is the "operator runs an older node" cell of the deployment matrix,
proven rather than reasoned about.

### The IPv4 regression rig — role `driver`

A machine with no IPv6 runs the pre-existing scenarios: a listed lobby refused when the registry is
unreachable, a direct join, the create screen, the lobby room and invite, and the direct-connect dialog
(which now also proves `[2001:db8::1]:8999` survives being remembered and redisplayed).

The same role run against a **client built from `10d18a7a`** produces the same lobby log: the distinct
`[LobbyManager]`/`[Reachability]`/`[STUN]`/`[UPnP]`/`[Firewall]` lines, with numbers normalised, are identical
between the two builds (12 distinct lines). That is the "byte-for-byte unchanged on a machine with no IPv6"
acceptance criterion, measured instead of asserted.

### What still needs two machines

Loopback can prove every code path but not a carrier's behaviour, and this machine has no global IPv6
address (only `fe80::` and `::1`), so these remain:

1. a **v6-only Windows host** (IPv4 disabled on the adapter): STUN and UPnP are never called, the room shows
   the IPv6 note instead of a router sentence, and the lobby publishes and is joined. The logic is
   unit-tested (`LobbyEndpointPlan.NeedsRouterMapping`) and the rendering is not.
2. **dual-stack across networks** and **dual-stack on one LAN**: that the IPv6 route is taken and needs no
   punch, read from the join line.
3. **A host on a filtered IPv6 network**: the rig proves the node drops a filtered endpoint, but only a real
   filtered adapter exercises the host's own refusal sentence.
4. A long-lived lobby against a **temporary-address rotation** (days, not minutes).
