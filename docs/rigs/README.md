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

`tools/rig/second-vantage.sh [--mapping reuse|translating] [--peers 0|1]` runs two registry nodes on the internet
namespace, with the host's router carrying the punch a host must send and losing its second one. With `--peers 0` the
lobby is refused (`MappingUnmeasured`: the outside port was measured at one destination only); with `--peers 1` the
second node is the second destination, the host punches its probe socket too, and the lobby is published with
`sources 2 of 2` and `mapping Reused`. Measured on `--mapping translating`: refused with one node (host exit 2), published
at the translated outside port with two, and **the guest did not join it** (exit 6) — that shape is listed as `PunchOnly`
while a punch does not admit a player, and it is recorded as an open defect rather than accepted.

What it cannot show: a real ISP's carrier-grade NAT, a real router's firmware, a real VPN exit.

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
  a lobby on port 8999. (It reported 24 open slots for a control command that asked for 8, which is worth a look on its
  own: the lobby's own slot count comes from the map, and whether `maxPlayers` is supposed to cap it is not stated.)
- A joiner inside the host's own namespace reaches it: `answer answered 9 ms (peer 0 accepted the attempt after 1 tries)`.
- A joiner inside the player's namespace, which is behind its own NAT, gets `answer silent` with no path open toward the
  host — which is the failure the whole ticket is about, reproduced on one machine in about six seconds.
- The same joiner, against the same lobby, answers in **8 ms** once the host's router forwards the port. So the silence
  above is the host's router refusing unsolicited traffic, and not the rig failing to carry packets.
- A listed lobby whose forward is withdrawn stops claiming `Open` on the registry **40 s** later and is listed as
  `PunchOnly`, with the host and the lobby room told (`tools/rig/reachability-withdrawal.sh`). A forwarded lobby is listed
  as `Open`: the class a listing states is derived from the cold answer the node measured, not from the address's family.

That last distinction cost a rig fix: the internet namespace routes between the two routers' segments, and without
`ip_forward` there every "blocked by NAT" result would have been "blocked by the rig" — a test that reports the right
symptom for the wrong reason is worse than no test, because it makes a fix look justified.

What is still missing for a complete lobby join in the rig: a host *client* to take the admin seat and publish the
lobby. The registry's punch is triggered by that client's lobby commands, so the rig cannot yet exercise the
punch-and-admit path end to end — only the transport answer, the NAT gating and the authority's own behaviour.

### What the host client needs

The client already carries the harness for this (`Testing/LobbyDirectConnectHarness.tscn`), with `--role=host`,
`--mode=listed`, `--dead-registry` and a done-file, and its listed mode calls the real `HostLobbyAsync`. Two gaps stand
between that and a rig run:

1. **A way to point it at the rig's registry.** The harness can only make the registry unreachable
   (`--dead-registry`); a `--registry=<url>` argument setting the lobby's server list is a small harness change.
2. **The authority the client spawns.** `ListenServerTarget` already appends no extension off Windows, so a file named
   `Realm.ListenServerHost` beside the authority project satisfies it — a one-line wrapper that execs the Linux Godot
   build with `--headless --path` is enough, and needs no export template.

Beneath both sits the cost of running the game client itself: it is a Godot project with full assets, so its first
headless launch on this checkout has to import them. That is minutes of one-off work on `/mnt/d`, and it is why this is
the next step rather than part of the runs recorded above.

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

## Where the end-to-end run stops today

With the rig up, a registry running in `realm-public` and the client hosting a listed lobby in `realm-host`, the
sequence now reaches further than it did and stops at a repeatable point:

- the registry answers on `203.0.113.1:5000`;
- the client starts, imports assets, measures its NAT and classifies the network;
- the client spawns the resident authority through the launcher, and the authority writes to its log:
  `resident authority up pid=… build=v0.0.6` and `watching the screen that started this authority, pid=…`;
- then nothing. No control line reaches the authority (its log gains nothing after those two lines), the registry
  receives no request, and the harness writes neither its invite file nor its done file.

Two conclusions worth keeping:

- **The stall is between starting the authority and sending it the first command.** It is not the registry, not the
  punch and not the transport: nothing arrives at any of them.
- **UPnP discovery is not the cause.** The client logs a line on every exit path of `UpnpService.OpenPortAsync`, and
  none appeared, so the stall happens before the endpoint is opened. The rig's router is not an IGD and that call would
  eventually time out, but it is not what is being waited on here.

The registry publishes for Linux but its `appsettings.json` is not in the published folder: copy
`appsettings.template.json` beside `Realm.AdminServer.dll` before starting it, or it aborts on startup with
`StorageDirectory is required`.

### The authority is not what stalls

**Superseded:** these three sections were written before the harness scene was launched explicitly; hosting never began
in those runs. What follows is kept for the isolates it records, not as the current account.

Fed one control line on its own stdin, the authority does everything it is asked and does it immediately:

```bash
( printf 'REALM_CONTROL {"requestId":1,"kind":"StartMatch","mapFolderPath":"/opt/realm-rig/map","port":8999,"seed":1234,"token":"rigtoken","lobby":true,"maxPlayers":8,"mapName":"MapTemplate"}\n'; sleep 25 ) \
    | ip netns exec realm-host <godot> --headless --path Realm.ListenServerHost -- --log /opt/realm-rig/logs/manual.log
```

It prints `REALM_RESIDENT_AUTHORITY_READY`, logs `match start: lobby listening at 164 ms`, opens the lobby on port
8999 and answers with `REALM_CONTROL_REPLY {"accepted":true,"state":"Running",…,"port":8999,…}`. So on Linux the
authority reads its stdin, starts a match, binds the port and replies.

That moves the stall onto the client's half of the pipe: the client starts the authority (its log names the parent as
the client's pid) and the authority says it is ready, but no command is ever written. Two things are worth checking
next, in this order:

1. The console capture beside the authority log (`ServerConsoleCapture.ResolvePath`), which holds every line the client
   read from the authority's stdout. If `REALM_RESIDENT_AUTHORITY_READY` is in it, the client saw readiness and failed
   to write; if it is not, the client's reader never received it across a pipe that Godot opened.
2. Whether the process the client spawns is the process that answers. The launcher runs one Godot binary, but the mono
   build may hand off to another process, and a client waiting on the first one's stdout waits on a pipe nobody writes.

Checked on this checkout: the console capture beside the authority log
(`local_authority.listen_server.<pid>.log.console`) **does** contain
`REALM_RESIDENT_AUTHORITY_READY`, so the client received readiness and the reader half of the pipe works. In the same
run the authority's own log still holds only `resident authority up` and `watching the screen…`, which means the command
the client meant to send after readiness never reached it. The remaining question is therefore narrow: whether
`ResidentAuthority` enqueues the command and its writer thread never writes, or writes into a pipe whose other end is a
different process than the one answering.

### The listed host never reaches hosting

`PrepareHostedLobbyAsync` opens with `RunNatTypeTestAsync`, which prints `Starting STUN NAT Type Test...` as its first act.
A host client started for this scenario prints that line **once**, and the startup NAT test is what printed it — the one
hosting would print a second time never appears, nor does the harness's own `The registry is set to …` line. So the run
is not stuck in the lobby path at all: it is stuck in `GameContext.EnsureServices()` before the harness takes its first
step, which is also why the authority is already up while nothing else happens (the resident authority belongs to the
session, not to hosting).

That reframes the remaining work: the rig needs the headless client to finish its own startup with no internet. The
startup code that reaches for the network — the update check, the server list and whatever else `EnsureServices` awaits —
has to either bound its waits or be satisfiable from inside the rig, and only then does a listed host get as far as
opening a port and talking to the registry.

## The listed join, measured

With the scene launched explicitly, a host publishes behind a router with no forward and a guest reaches it by lobby id.
The guest gets as far as asking the registry where the lobby is, reporting its own endpoint, and knocking twice; the
authority answers neither time:

```
[LobbyManager] Joining Lobby b427a874-… via registry server...
[LobbyManager] Reported this screen's endpoint to 203.0.113.1:60533 from the socket joining the lobby on port 52302.
[LobbyManager] Joining 203.0.113.2:52302 again after 'The lobby's authority did not answer in time.' (attempt 2 of 2).
```

The address to compare against is the one the host itself advertises in the same run:
`mode=listed direct=False address=10.10.0.2:8999 public=203.0.113.2:52302`. Its earlier registration was measured at
**203.0.113.2:64076**, and the mapping refresh that runs every 20 s is the only thing that could have moved it. A
registered endpoint that the router no longer maps is knocked on in vain, so the first thing to check is whether the
refresh keeps the port the registration measured or hands back a new one.

**Correction to the endpoint drift above:** measured again inside one run, the registration and the host's own
advertisement agree exactly (`The registry reached this lobby's game port and measured 203.0.113.2:40874` and
`public=203.0.113.2:40874`). The 64076/52302 pair came from two separate runs, each of which gets its own mapping port,
so there is no drift to fix. What remains open is the punch itself: the guest knocks on the address the host really
publishes and the host's router still answers nothing, so the datagram the host is supposed to send toward the joiner -
the one that would open its router for that joiner's address and port - is either never sent or sent to an endpoint the
joiner's own mapping does not match.

## The punch works; admission does not (yet)

With `JoinerPunchRelay` keeping a ticket alive instead of consuming it on the first report, one rig run showed the
whole reachability path working through two routers with no forwarded port:

```
relay: 2 reports relayed (was 1)
host:  2 punches at the joiner (was 1)
host:  peer 1656090650 connected (was no peer at all)
guest: exit 6 - "The lobby's authority did not answer in time."
```

So publish, register, discover, report, punch and connect are all proven. What is left is not the network: the joiner's
transport is accepted and the lobby still never seats or describes it before the attempt's budget runs out. Look at
admission - the lobby accepting the peer's identity and sending its state back - and at whether a cold lobby's first
admission is slower than one attempt's budget.
