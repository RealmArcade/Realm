# Hosting behind a restrictive network

Realm publishes a lobby by having a lobby server (a registry node) reach the host's game port from the internet. That one
measurement decides everything a host and their friends are told: whether the lobby can be listed at all, what class it is
listed with, and what to change when somebody cannot get in. This page is the part a player needs: which networks work,
which do not, and what to do about each.

## What has to be true before a lobby can be listed

1. **This machine holds the port.** Realm listens on a UDP port (`8999` unless it says otherwise) and publishes the port it
   is actually listening on. If that port is already taken by another lobby on the same computer, Realm takes another port
   and tells you which one. The port is stated in the lobby's diagnostics block, in its invite message and in the address
   the browser lists the lobby at.
2. **A lobby server can reach that port.** The host rehearses a join through the lobby server before the lobby is listed.
   The registry node sends to the port; if nothing comes back it sends nothing further and the lobby is not published.
2b. **Windows lets that one program through, once.** The allowance Realm asks for is one inbound UDP rule, for the Realm
   executable only — not for every program, and not for every port. A build that could not name its program used to add a
   rule per port (`Realm RTS Lobby UDP <port>`); those are removed the next time you host, so a machine that hosted under
   an older build ends up with exactly one rule. A rule you made yourself is never touched.

3. **The address the lobby server measured is one a player elsewhere can use.** A lobby checked from inside its own
   network, or measured at a private or loopback address, proves nothing about a stranger and is not listed.

A lobby that fails 2 or 3 is still playable: Realm says why it was not published and hands you the address to send to the
people you want to play with. That is not a fallback that "sometimes" works — it is the honest result of the measurement.

The one exception is the volunteer relay below. A lobby whose own port nothing reached is still listed when the lobby
server pools at least one volunteer, with the class `Relayed`: no player is ever told to connect to the host's own port,
because nothing measured it; they are told to connect through a player's machine that was measured. That listing is refused
when the pool is empty, so a lobby is never published with no way in.

A listed lobby stays measured. Every 20 seconds Realm refreshes the router's mapping and every 45 seconds it measures the
lobby again with the same lobby server. If the port stops answering — an UPnP lease expired, the forward was removed, the
machine's address changed — the listing moves down to `PunchOnly` (players must punch, and the lobby says so) or to
`Unverified` (nobody can be told where to connect, and the browser hides it unless asked). The lobby room is told at the
same moment, and the host is told what happened. Measured on the one-machine rig: withdrawing the forward behind a listed
lobby moved it from `Open` to `PunchOnly` 40 seconds later, while the players who had already punched kept playing.

## The classes a lobby is listed with

| What the lobby server measured | What Realm calls it | What it means for a player |
| --- | --- | --- |
| The port answered without the host sending anything first | `Open` | A router or firewall already forwards the port. A join needs nothing special. |
| The port answered only after the host punched the lobby server | `PunchOnly` | The ordinary home router. Both sides send a packet first, which opens the router for that one peer. Most players join this way and never notice. |
| The router gave the game port a different outside port for every destination | not listed | A punch can open the way for one endpoint and no other, so no player can be told where to connect. This is the case this page exists for. |
| Nothing reached the host's port at all, and the lobby server pools a volunteer that answered a stranger | `Relayed` | The join is carried through another player's machine. It works with nothing installed and costs the joining player the volunteer's extra round trip, which the lobby room states before the match starts. |
| The router translated the outside port, and the second check that shows whether it is the same for every player did not complete | not listed | Nothing is known to be broken, and nothing was shown to work either: the lobby is not listed at an outside port nobody measured. Hosting again, or forwarding the port, settles it. |

### Which outside port a lobby is listed at

A router almost always gives the game a port of its own — anything other than the one the game listens on. That is
expected and needs no fixing: the lobby is listed at the outside port the router exposed, because that is the port every
player's punch reaches, and Realm states which one it is in the diagnostics block.

What cannot be fixed by punching is a router that makes a *different* outside port for every peer. To tell those two
apart, the lobby server punches two of its own ports and compares the outside port each punch arrived from: one port for
both means the router keeps one mapping for every player, and two ports mean it makes one per player. Both punches are
sent by the host's game port, so they cost nothing and open nothing — a router that dropped them says so instead of
assuming the worse case.

`PunchOnly` is not a problem and does not need fixing. The lobby is listed, players join, and nothing has to be changed on
the router. A lobby stays listed while it is being joined: if enough players fail in a short window, Realm stops claiming
the lobby is reachable and tells you which step of the join failed and what to do.

## Tier 1 — a network with an inbound path (the usual case)

Nothing to do. Enable UPnP on the router if it is offered and Realm will ask for the mapping when you host. A router that
refuses or has no UPnP still works through punching (`PunchOnly`), which is what most lobbies are.

If you want the lobby to read `Open` and to work even for a player whose own router is the awkward one, forward the UDP
port by hand:

1. Host a lobby once and read the port from the diagnostics block (**COPY NETWORK DIAGNOSTICS** in the lobby room).
2. On the router, forward that UDP port to this computer's address on your network. Forward UDP, not TCP.
3. Host again. The lobby should now read `Open` in the browser row.

Forwarding is a rule on the router that faces the internet. On a network with two routers in a row — a provider's box in
front of your own — the rule belongs on the outer one, and Realm will tell you when it sees a forward that exists but is
still not reachable from outside.

## Tier 2 — a network with no inbound path at all

This is the case the earlier tickets call hostile: carrier-grade NAT on a mobile hotspot, an office or hotel network, a
VPN exit that puts you behind somebody else's NAT, or an ISP that gives every customer a shared address. Nothing you do in
your own router can help, because the router that would have to forward the port is not yours. Three things work, and the
first costs nobody anything.

### A volunteer relay (nothing to install)

Realm asks the lobby server which players are currently willing to carry other players' connections, and hands one of them
the lobby's join. That player's machine forwards the match traffic through a UDP port that the lobby server measured as
reachable from outside; the host and the joining player encrypt the traffic between themselves, so the volunteer moves
bytes it cannot read and cannot join, record or alter the match it carries.

- **Host:** leave `Accept relayed connections` on (Settings → Other). It publishes exactly the lobby this build published
  before relays existed when it is off, and a lobby that reads `Open` or `PunchOnly` is joined directly either way — a
  volunteer is spent only on a join the direct path could not finish.
- **Joiner:** nothing to do. The join is tried at the lobby's own address first and retried through the volunteer only when
  that attempt did not answer.
- **Volunteer:** off by default. `Carry other players' connections` in Settings → Other states a bandwidth budget, how many
  connections may be carried at once, what the machine has carried so far and a `Stop carrying connections now` button. A
  volunteer is only offered while its own port measures as reachable from outside and while its player is not in a match; it
  stops when the player starts one. Its own match tick is never touched, because the forwarding runs on a lane of its own
  and drops rather than queues when a budget is reached.
- **What it costs:** the volunteer's round trip is added to the join, and it is measured rather than described — the lobby
  room states how many paths carry the connection and what the best of them measured, and the network diagnostics block
  names every path with its round trip and loss. Nothing claims a relayed connection is as good as a direct one.

A host whose lobby cannot be reached directly is listed with the class `Relayed` only while the pool holds a volunteer. If
nobody is volunteering, the lobby is not listed and Realm says so, which is the same honesty the direct checks get.

### An overlay network (easiest, costs every player an install)

A mesh overlay such as Tailscale, ZeroTier or Netbird gives each machine an address the others can reach, and carries the
traffic itself. The host and **every player** install it and join the same network; the lobby is then played at the
address the overlay gives the host, which behaves like a normal reachable network.

- It works no matter what the ISP does, which is why it is the answer for a host with no inbound path of any kind.
- It costs every player an install and an account with the provider. Free tiers exist and are usually enough for a few
  machines; the tier limits and prices change, so check the provider's own pricing page on the day rather than trusting a
  number written here.
- Realm detects the overlay address as an ordinary address. Nothing in the game has to be configured for it.

### A VPN that forwards a UDP port (costs nobody else anything)

Some VPN providers reserve an address and will forward one UDP port for the account, entered in the provider's dashboard.
The host connects the VPN, asks the provider to forward the lobby's UDP port, and hosts. Everyone else joins the address
the lobby server measured, with nothing installed.

- A VPN **without** a forwarded port makes hosting worse, not better: the tunnel is another NAT in front of the one you
  already have, and the forwarded tunnel is exactly what turns "sometimes" into "always". If the provider's dashboard has no
  port forwarding, do not use it for hosting.
- This is the pattern behind the failure this branch reproduces: the host registers and every join is lost, because the
  tunnel is a NAT that nothing was opened through. Realm now says so rather than leaving players to guess.
- Pricing and forward counts differ per provider and change often. Check the provider's own page; the two questions that
  decide whether it will work are "does the account get a dedicated address" and "can one UDP port be forwarded to it".

### Which of the three is needed

The relay is the answer for a host with no inbound path, and it costs nobody an install. The forwarded VPN tier remains
for the case the pool cannot serve — no volunteer is online, or the one offered is not fast enough for the match. The
overlay tier stays useful for a group that would rather not send its match through anybody else's machine at all.

## Decisions the relay records

- **Rejected carriers.** A TCP relay is refused: a head-of-line-blocked stream carrying a 30 Hz snapshot stream turns one
  lost packet into a stall the snapshot timeline cannot mask. An external TURN service is refused: it is a hosted
  dependency this project does not run, and a pool of volunteers already answers the question it would be bought for.
  Browser and WebRTC transports are refused: the game is a desktop build whose transport is ENet over UDP, and neither
  brings a path a volunteer cannot already carry.
- **Encryption scope.** Only relayed paths are wrapped in the connection's own authenticated envelope. The direct path keeps
  exactly the datagrams it sent before this feature existed, which is what makes "with no volunteer, nothing changed" true
  by construction rather than by a timeout.
- **Peer identity.** Once a path can be a relay, a socket address no longer says who a player is: the connection token is the
  identity, and a relayed player arrives at an entry of its own rather than at the address a direct peer would have.

## Troubleshooting: symptom, phase, what to try

A failed join is reported at the step that broke, and the host is told that step in the lobby's chat.

| Symptom | Step that failed | What to try |
| --- | --- | --- |
| The lobby is listed but nobody can join | `answer` — the lobby's port never answered the player | The host forwards the lobby's UDP port, or turns on UPnP; if the router already forwards it, check the rule is on the outermost router. |
| The lobby is listed, then stops being listed | several players failed in a minute | The listed class was disproved by real players. Let the host act on the failures named in chat and host again. |
| A join fails in under a second with "not on that network after all" | the host's own-network address | Two networks that number themselves the same look alike from inside. The join falls back to the lobby's outside address on its own; if it still fails, the two players are not actually on one network. |
| Nobody can host at all | `registry` | The lobby server cannot be reached, so nothing could be measured. Fix the lobby server address in the server list, or host a lobby that is not listed and send the address. |
| Joins work on the local network only | `answer` from outside | The router has no inbound path that the lobby server could use. Leave `Accept relayed connections` on so the lobby server can carry the join through a volunteer, or use an overlay network / a VPN with a forwarded port. |
| Hosting is refused with "no volunteer it measured as reachable" | `registry` — the lobby server pools nobody | The lobby's own port reached nothing, so the only way in would be a volunteer and this node has none. Give the lobby an inbound path as above, or use a lobby server that pools volunteers. |
| The lobby reads `Relayed` and nobody can join it | `answer` — the volunteer carried nothing | The volunteer has gone since the lobby was listed. Host again once one is online; the lobby room's connection line names the paths that were offered. |
| The port in the diagnostics block is not 8999 | none — this is normal | Another lobby on this computer held 8999 first. Every address Realm publishes states the port it is really on. |

## Reading a host's report

The lobby room's **COPY NETWORK DIAGNOSTICS** button copies one block: the port the lobby is listed at, the port its
transport holds, the address this machine measured, the address something outside confirmed, the reachability class, the
router's mapping behaviour, the NAT and firewall state, the lobby server, and the last failure either side saw. The two
ports differing is normal — it means the router translated the port and the lobby is listed at the outside port players
reach. What is worth looking at is `router mapping: Unknown` next to two differing ports: that is the case above where
nothing was measured, and it is the one that is not listed.
