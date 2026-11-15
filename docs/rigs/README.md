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

A lobby still needs a map folder with a compiled script, because the authority refuses to start a match without one.
