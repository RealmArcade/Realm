# Network rigs

Three rigs cover a join failure, in the order they should be used. Only the third needs a second machine.

## 1. In-process NAT simulation (`Realm.Network.Tests`)

`SimulatedNat` and `FakeGamePort` put a real UDP relay between the test and the port it judges, so filtering and port
translation are exercised against real sockets without leaving the test process. It answers the questions the publish
gate rests on: does a port that answers only after a punch get published, is a silent port refused, is the published port
the one the punch exposed, and does a router that gives every destination its own outside port cost a lobby its listing
while one that keeps a single mapping is published at that mapping's port. `SimulatedNat` models both mapping behaviours,
so `ReachabilityRouterTests` states each one as its own case.

What it cannot show: a real ENet handshake or the operating system's firewall.

## 2. Namespace rig (`tools/rig/realm-rig.sh`)

The single-machine rig. Five network namespaces: a registry "internet" and two machines, each behind its own router,
with the routers doing real `MASQUERADE`. Run it in WSL as root; no desktop hypervisor and no elevation prompt.

```bash
tools/rig/realm-rig.sh up
tools/rig/realm-rig.sh status
tools/rig/realm-rig.sh mapping reuse|translating|per-destination
                                          # rebuild the routers: one outside port for every destination, that same one on a
                                          # port the router chose, or a port per destination
tools/rig/realm-rig.sh host  <command>    # run inside the host's namespace
tools/rig/realm-rig.sh player <command>   # run inside the player's namespace
tools/rig/realm-rig.sh public <command>   # run inside the registry's namespace
tools/rig/realm-rig.sh down
```

Addresses are from the documentation ranges (`203.0.113.0/24`, `198.51.100.0/24`) rather than private ones. They are
globally scoped, so a lobby server on this rig judges a host the way a public node does and the "never judge a caller on
its own network" rule is exercised rather than bypassed. Nothing leaves the machine: there is no route out.

What it proves: a datagram from the host reaches the registry, the registry cannot reach the host unsolicited, a punch
opens the path, and the published endpoint is the one the router exposed — under every mapping mode.

`tools/rig/reachability-withdrawal.sh [--mapping reuse|translating] [--wait 75]` is the other half: a lobby is published
through a forwarded port, the forward and the destination translation are withdrawn while the host keeps playing, and the
check reports how long the listing took to stop saying `Open`. It exits 0 only if the registry states the weaker class
within the wait, 5 if the lobby still claims `Open`, and 4 if the host never published. Measured: the listing moved from
`Open` to `PunchOnly` 40 seconds after the forward was withdrawn, the lobby room was told, and a stranger's first
datagram no longer reached the game port while the player who had already punched kept playing.

A translated outside port is measured by punching the same game port from two sockets of the node's own probe, so a
single-node deployment lists it without help: `listed-join.sh --mapping translating` published at `203.0.113.2:10000`
for a game port of 8999 and admitted the guest 10 times in 10. A scenario that deletes the `PREROUTING` DNAT while doing
that is deleting the translation, not a forward standing beside it: the outside port exists because the router
translates to it, so what such a run measures is a port that no longer exists, not a listening rule that is too strict.

What it cannot show: a real ISP's carrier-grade NAT, a real router's firmware, a real VPN exit, or a router that rewrites
the outside port of a flow without creating the reverse translation for it — a real router creates both together, which is
what the punch relies on.

## 3. Two machines

The sign-off. Same LAN with UPnP off, two ISPs with UPnP off, host on a phone hotspot, both machines on one hotspot (the
same-public-IP case that must not be mistaken for a LAN), and a VPN on the host with a different exit from the joiner.
A publish test must use a lobby server on another machine: a local node is refused by design, and that refusal is
correct.

## Running the pieces on Linux

The authority is a Godot project, but its managed side builds and runs on Linux, which is what makes the namespace rig
usable:

```bash
dotnet build Realm.ListenServerHost/Realm.ListenServerHost.csproj -c Release
/opt/realm-rig/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64 \
    --headless --path Realm.ListenServerHost -- --log /tmp/authority.log
```

It prints `REALM_RESIDENT_AUTHORITY_READY` and then waits for a control command on stdin, exactly as it does on Windows.
The registry is a plain ASP.NET application and publishes for `linux-x64` without changes.

A lobby still needs a map folder with a compiled script, because the authority refuses to start a match without one. Compiling one on Linux needs two things beyond the SDK:

```bash
dotnet workload install wasi-experimental
export WASI_SDK_PATH=/opt/realm-rig/wasi-sdk-25.0-x86_64-linux   # share/wasi-sysroot must exist
dotnet publish MapTemplate/MapScript.csproj -c Release -o /opt/realm-rig/mapbuild
cp /opt/realm-rig/mapbuild/MapScript.wasm <map folder>/bin/script/
```

Then hand the authority a lobby over its control channel, from inside its namespace:

```bash
printf 'REALM_CONTROL {"requestId":1,"kind":"StartMatch","mapFolderPath":"/opt/realm-rig/map","port":8999,"seed":1234,"token":"rigtoken","lobby":true,"maxPlayers":8,"mapName":"MapTemplate"}\n'
```

A router can be given a forwarded port, which is the case a player who has done the work expects:

```bash
ip netns exec realm-host-router iptables -t nat -A PREROUTING -p udp --dport 8999 -j DNAT --to-destination 10.10.0.2:8999
ip netns exec realm-host-router iptables -I FORWARD 1 -i wan-host -o lan-host -p udp --dport 8999 -j ACCEPT
```

## Measured on this checkout

- The Linux build of the authority answers a controller, loads the map from the Linux-built `MapScript.wasm`, and opens
  a lobby on port 8999.
- A joiner inside the host's own namespace reaches it: `answer answered 9 ms (peer 0 accepted the attempt after 1 tries)`.
- A joiner inside the player's namespace, which is behind its own NAT, gets `answer silent` with no path open toward the
  host — which is the failure the punch path exists to close, reproduced on one machine in about six seconds.
- The same joiner, against the same lobby, answers in **8 ms** once the host's router forwards the port. So the silence
  above is the host's router refusing unsolicited traffic, and not the rig failing to carry packets.
- A listed lobby whose forward is withdrawn stops claiming `Open` on the registry **40 s** later and is listed as
  `PunchOnly`, with the host and the lobby room told (`tools/rig/reachability-withdrawal.sh`). A forwarded lobby is listed
  as `Open`: the class a listing states is derived from the cold answer the node measured, not from the address's family.
- A router that gives the game port an outside port of its own is listed at that outside port and joined:
  `listed-join.sh --mapping translating` published at `203.0.113.2:10000` and the guest was admitted in about 0.7 s, ten
  runs in ten. A lobby is judged by the node it registers with, on that node's own two punches, so its class is the same
  in a one-node and a many-node deployment.

That last distinction cost a rig fix: the internet namespace routes between the two routers' segments, and without
`ip_forward` there every "blocked by NAT" result would have been "blocked by the rig" — a test that reports the right
symptom for the wrong reason is worse than no test, because it makes a fix look justified.

## Driving the rig by hand

`realm-rig.sh up` creates six namespaces, and their names are the ones to use rather than a guess:

| Namespace | Role |
| --- | --- |
| `realm-host` | the hosting machine |
| `realm-host-router` | that machine's router (NAT, optional forwarded port) |
| `realm-player` | the joining machine |
| `realm-player-router` | that machine's router |
| `realm-public` | the internet between the two routers; the registry runs here |
| `realm-admin` | the administration node |

Two things to know before running a scenario by hand:

- **Run the whole scenario in one shell invocation.** A WSL that restarts reclaims every namespace, so a command that brings the rig up and a later command that uses it are not the same environment. Bring the rig up, start the registry, run both clients and tear down inside one script.
- **The host namespace needs a way out to a public STUN server**, otherwise the NAT measurement reports `Resource temporarily unavailable` and the host is classified from a partial reading. Starting the registry in `realm-public` before the client is the cheapest check that the path out works at all.

## The listed join, measured

`tools/rig/listed-join.sh` runs a listed host behind its router and a guest behind another, by lobby id, in one
invocation: `--mapping reuse|translating|per-destination` chooses the host router, `--forward` opens the game port,
`--block-punch`, `--fail-storm N`, `--lookalike-lan` and `--same-machine` are the failure shapes, and `--registry-only`
stops after the registry so a scenario can be driven by hand. Exit 0 is a guest admitted, 4 is a host that never
published, 6 is a guest that could not join.

The path as it stands, ten consecutive runs of `--mapping translating` (a router that gives the game port an outside
port of its own and keeps one mapping for every destination): 10/10 exit 0, two seats each, published at
`203.0.113.2:10000` for a game port of 8999, admitted on the first attempt in about 0.7 s.

`tools/rig/reachability-withdrawal.sh` publishes through a forward and withdraws it under a live host to check the
listing stops claiming `Open` within a minute.

Both are checks, not a harness of their own: the clients are the real Godot build through
`Testing/LobbyDirectConnectHarness.tscn`, and the registry is the real node published for linux-x64. Rebuild both
artefacts before believing a run — a stale node binary reads exactly like a code defect, which has cost this project
more than one afternoon.
