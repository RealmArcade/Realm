# Network rigs

Three rigs cover a join failure, in the order they should be used. Only the third needs a second machine.

## 1. In-process NAT simulation (`Realm.Network.Tests`)

`SimulatedNat` and `FakeGamePort` put a real UDP relay between the test and the port it judges, so filtering and port
translation are exercised against real sockets without leaving the test process. It answers the questions the publish
gate rests on: does a port that answers only after a punch get published, is a silent port refused, and is the published
port the one the punch exposed.

What it cannot show: a real ENet handshake, the firewall, or a router whose mapping is made per destination.

## 2. Namespace rig (`tools/rig/realm-rig.sh`)

The single-machine rig. Five network namespaces: a registry "internet" and two machines, each behind its own router,
with the routers doing real `MASQUERADE`. Run it in WSL as root; no desktop hypervisor and no elevation prompt.

```bash
tools/rig/realm-rig.sh up
tools/rig/realm-rig.sh status
tools/rig/realm-rig.sh mapping random     # rebuild the routers with a per-destination mapping
tools/rig/realm-rig.sh host  <command>    # run inside the host's namespace
tools/rig/realm-rig.sh player <command>   # run inside the player's namespace
tools/rig/realm-rig.sh public <command>   # run inside the registry's namespace
tools/rig/realm-rig.sh down
```

Addresses are from the documentation ranges (`203.0.113.0/24`, `198.51.100.0/24`) rather than private ones. They are
globally scoped, so a lobby server on this rig judges a host the way a public node does and the "never judge a caller on
its own network" rule is exercised rather than bypassed. Nothing leaves the machine: there is no route out.

What it proves: a datagram from the host reaches the registry, the registry cannot reach the host unsolicited, a punch
opens the path, and the published endpoint is the one the router exposed — under either mapping mode.

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
