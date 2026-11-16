# Manual test plan: a match carried through a volunteer

What this plan covers is the part no single machine can show. The one-machine rig has already proved the path layer under
real sockets (duplicate suppression, sequencer, budgets, mid-stream loss), the lobby admit of a relayed player, a whole
match's snapshot stream through two volunteers, and a mid-match switch from the carrying volunteer to the spare with the
ENet peer untouched. What it cannot prove is behaviour across two real networks: a host nothing can reach, a joiner behind
a different router, and a volunteer that is a third machine on a third network.

Everything below is stated as what to do, what you should see, and where to read it. Read
[Hosting behind a restrictive network](Hosting_Behind_Restrictive_NAT.md) first for what the classes mean.

## What you need

| Role | Machine | Notes |
| --- | --- | --- |
| Host | A machine whose lobby the registry cannot reach at all | Carrier-grade NAT, a hotel or office network, or a VPN without a forwarded port. The lobby must be listed `Relayed` for this plan to test anything. |
| Joiner | A different network from the host | A normal home router is fine; it does not need to be reachable. |
| Volunteer | A third machine on a network where an outside port answers | Its lobby row must show it pooled before you start; see "Prepare the volunteer". |

One lobby server both the host and the joiner can reach. `-` in a log line means the value was not measured; it is never
a zero.

## Prepare the volunteer

1. Settings → Other → enable `Carry other players' connections`, set a bandwidth budget and a connection ceiling, apply.
2. Open Settings again and read the status line under the controls. It must say the machine is offering to carry up to N
   connections on port P, within B bytes a second. If it says the lobby server has not pooled it, or names a measured
   reachability reason, that machine cannot relay: fix its network or use another volunteer. Do not continue with a
   volunteer the node did not pool — the lobby that needs it will be refused publication instead.
3. Note the port it states. That port is what the lobby server measured; it is not the game's port.

## Scenario 1 — a lobby nothing can reach, joined through the volunteer

1. On the host, leave `Accept relayed connections` on and host the usual lobby.
2. The lobby must appear in the browser listed `Relayed`, and the lobby room's connection line on the joiner must read
   `Carried through N volunteer(s) over M path(s): RTT ms, loss%`. A lobby that could not be listed states why instead,
   and that is a failure of this scenario, not a pass with a warning.
3. On the joiner, join normally. Nothing is configured on that machine.
4. Confirm the match is played: both screens see the countdown, the match starts, units move, and the joiner's
   presentation delay is the measured one, not a frozen picture. The joiner sees the volunteer's extra round trip; a
   stutter that matches the loss number in the diagnostics block is expected, a still frame is not.

Where to read it:

- **Authority log** (`local_authority.listen_server.<pid>.log` in the game's log folder):
  - `relay: carrying a join through N volunteer(s): <ids>` — the plan reached the authority.
  - `relay: loopback port <p>: path=1 was opened (not answered yet); path=2 was opened (not answered yet)`.
  - `relay: loopback port <p>: path=1 answered again (rtt … ms, jitter … ms, loss …%); the best path moved none->1 (…)`.
  - `peer <id> admitted to the lobby as seat 2 ('…'), member` — the relayed player was seated exactly like a direct one.
  - `snapshots: N carried as deltas on the current stream, …` when the match ends.
- **Lobby room diagnostics** (`COPY NETWORK DIAGNOSTICS`), on the joiner:

  ```
  carried through: volunteers volunteer-a, volunteer-b
  relay shim port=54150 delivered=1832 dropped=0
  relay token=… paths=2 duplicates=14 unmoved=0 best=1
  relay path=1 carrier=UdpTunnel open=True live=True rtt=41ms jitter=3ms loss=0.4% sent=910 received=906 duplicates=7 dropped=0 rejected=0 bytes=…
  relay path=2 carrier=UdpTunnel open=True live=True rtt=46ms jitter=6ms loss=1.1% sent=910 received=897 duplicates=7 dropped=0 rejected=0 bytes=…
  ```

  `duplicates` above zero with each game description still applied once is the dedupe working, not a fault. `unmoved`
  above zero means datagrams found no path at all and is a fault.

## Scenario 2 — the volunteer leaves mid-match

1. With the match running, on the volunteer machine press `Stop carrying connections now`.
2. The match must continue on both screens. The joiner's control of its units must keep working and the host must keep
   seeing the joiner move.
3. Open the joiner's diagnostics. `carried through` must still name the volunteers and at least one path must be
   `live=True`; the path that carried the connection before will read `live=False` with a high loss for up to a few
   seconds before it is retired.

Where to read it:

- **Authority log**: `relay: loopback port <p>: path=1 stopped being answered (rtt 0 ms, jitter 0 ms, loss 100.0%)` and
  then `the best path moved 1->2 (rtt … ms, loss …%)`. If a second volunteer was offered, that is what the connection
  must move to.
- **Both screens**: no reconnect, no `Local peer … changed`, and the match does not end. The joiner's own log must not
  show a new peer id.

Pass only if the same peer kept playing. A match that ends, or a joiner whose screen re-enters the lobby, is a failure:
that is exactly what the local endpoint per connection exists to prevent.

## Scenario 3 — the volunteer stops and nothing replaces it

1. Prepare one volunteer, host the relayed lobby, join it, then stop the volunteer and leave no other volunteer running.
2. Within about four seconds every path the joiner holds has stopped being answered. Expected result: that seat leaves the
   match and the host sees it leave; the lobby stays up for other players. The seat may take a few seconds longer than the
   paths do, because the transport's own timeout decides when a peer is gone; what must not happen is a screen that keeps
   reporting a carried connection after no path answers.
3. Host again with the pool empty: the lobby must be refused publication with the reason about no volunteer it measured as
   reachable, not listed `Relayed` with no way in.

## Scenario 4 — nothing is volunteered, and nothing changes

1. Turn volunteering off everywhere, empty the pool, and repeat a direct host/join on two reachable networks.
2. Publishing, admission, the snapshot stream and the lobby's diagnostics must read exactly as they did before relays
   existed: `carried through: direct (volunteer pool not involved)`, no `relay:` lines in the authority log, no
   `[Relay]` lines on either screen.

This is the regression check that matters most. Any change in direct play is a defect in this feature regardless of what
relaying does.

## Scenario 5 — a machine that cannot relay

1. On a machine behind a network that drops strangers (symmetric router, carrier-grade NAT), enable volunteering.
2. It must not be pooled. Its status line must say it is offering bandwidth but the lobby server has not pooled it, or
   name the measured reason the offer was refused. The forwarding socket is kept open and registration is retried, which
   is intentional; what must not happen is a status line claiming connections are being carried.
3. Hosting from that machine still lists `Relayed` only when some other volunteer is pooled.

## What to record for a bug report

- Both screens' `COPY NETWORK DIAGNOSTICS` blocks, copied while the fault is visible.
- The authority log from `match start` to the end of the match.
- Which scenario, which step, and the last line each side wrote before it broke.

That is enough for two hosts' outputs to be compared line by line, which is what the block and the log lines are shaped
for.
