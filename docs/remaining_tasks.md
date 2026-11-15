# Remaining tasks: P2P hosting resilience

This document is a self-contained handoff. It exists so that the remaining work on
`fix/p2p-connection-diagnosis` can be completed by an agent without the user answering questions, and without
any claim being made that was not measured.

Read it top to bottom before touching code. Then read `AGENTS.md` and `docs/network_host_resilience_task.md`.

---

## 1. Mission

Finish the branch so that a host behind a hostile network — a home router that drops unsolicited traffic, a phone
hotspot, carrier-grade NAT, or a VPN exit — can publish a lobby, and a joiner can find it and be admitted, with no
manual port forwarding. Then make the failure mode honest: when a host or a joiner cannot be reached, the screen that
tried must say why in specific terms.

**Definition of done for this document:** every item in section 6 is either completed with the measurement named in
its acceptance line, or is explicitly reported as blocked with the measurement that blocked it. No item may be marked
done on the strength of code inspection alone.

**Do not ask the user for feedback between steps.** Work the plan, measure each change, record the result, and only
stop at the end with a report in the shape of section 9.

---

## 2. Ground rules

- Follow `AGENTS.md` exactly. No comments unless a struct needs its single `<summary />` or the API needs XML docs.
  No `#region`, no `sealed`, no `internal`, self-documenting names, English everywhere.
- **Compile after every change.** Neither branch nor repository state may be left with a build error.
- **A claim without a measurement is not allowed.** Every "fixed" in your report needs a log line, an exit code, or a
  conntrack entry behind it. If you could not measure something, say so in those words.
- **Do not weaken a check to make it pass.** Do not add retries, timeouts or suppressions as a way of turning a red
  result green; fix the cause or report it.
- Unit tests are only for the items that explicitly ask for them (A10). Do not add a suite for anything else; the
  project deliberately keeps its suite small.
- Commit per coherent change, in the repository's existing style, with no automated attribution footer.
- Never `git push --force`, never reset, never touch `origin/main`.

---

## 3. Where the branch stands

`fix/p2p-connection-diagnosis`, head of the branch when this section was written, working tree clean. The whole of P0 to P5
below is measured in the namespace rig; P6 is written; P7 to P9 are open. The per-item evidence, including the exact log
line or exit code that closes each one, is in the status table under `The Checklist` in `docs/network_host_resilience_task.md`.

### Fixed, and measured in the rig

- **A lobby is listed at the port its transport holds.** The register route stores `req.HostPort`, refuses a lobby that
  does not state one, and the client sends the bound port rather than the port a check observed. A joiner's conntrack now
  shows one assured flow instead of two unreplying ones, and `guest.done` reads exit 0 with a seat count.
- **The joiner punches the lobby before it reports itself.** A router that refuses unsolicited traffic admits the host's
  answer only from an endpoint this side has already spoken to, and the host punches the moment the registry reads the
  report. Punching first from the same port removed a race that failed one join in two before it.
- **A look-alike local address is tried, not trusted.** Two networks that both number themselves `192.168.1.0/24` no
  longer cost the join two full timeouts: the near address gets 2.5 s and the join falls back to the lobby's outside one.
- **Reachability is measured, not certified.** A cold probe from a socket the host never punched classifies a lobby
  `Open`; silence after a successful punch is `PunchOnly`; an answer from inside the lobby's own network, or at an address
  inside it, publishes nothing. A router whose mapping is per destination is classified `Unverified`, is not listed, and
  its host is told what its router does and what to change.
- **A failed join names the step that broke.** The joiner reports the phase, endpoint, attempts and elapsed time to the
  registry, the registry relays it to the lobby's host WebSocket, and the host's chat gains one line authored by the
  authority. Identical failures collapse, six lines a minute per lobby and eight reports a minute per source are the caps,
  and three distinct failures in a minute stop the lobby claiming a reachability its players disproved.
- **A listed join is reliable, measured over ten runs.** Ten consecutive `listed-join.sh` runs all ended with the guest
  admitted (`2 seat(s) after admission`), each in about a second, against one failure in two before the joiner was made to
  punch before it reports itself. Thirteen consecutive successes follow the change; at the earlier rate that would be a
  1-in-8192 coincidence, so the race is gone even though the exact conntrack state that lost the old runs was never caught.
- **A host can hand over one block of text.** `COPY NETWORK DIAGNOSTICS` states both ports, the measured and the confirmed
  address, the class, the mapping behaviour, the NAT and firewall state, the lobby server and the last failure either side
  saw.
- **Two lobbies on one machine collide nowhere.** The second lobby takes a system port and every address it publishes
  states that port; the map download and the diagnostic listener ask the system for a port instead of deriving one.

### Still open

- A7 (firewall rule migration), A9 (executor capabilities), A10 and G1-G2 (contract tests, which this project keeps out of its suite on
  purpose), B4 (only advertise a reachable seeder), B5 (audit of every address consumer), C3, C4, C6, C8, C11, C12, E1
  (written, not measured), F (relay), H4 (needs F2), I1/J2 (the CLI joiner exists; its phase output has not been re-checked
  since the phases were named), P9's remaining review of the older sections of the checklist.

## 4. The test rig

Everything below runs on one machine. It is the only rig in the project and it is enough for all but one item in
section 6.

### Requirements already installed in this WSL (Ubuntu, run as root)

- `dotnet` (`/usr/local/dotnet` or `/opt/dotnet` on `PATH`).
- Godot 4.7 mono Linux: `/opt/realm-rig/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64`.
- WASI SDK at `/opt/realm-rig/wasi-sdk-25.0-x86_64-linux`, plus a compiled map at `/opt/realm-rig/map`
  (`bin/script/MapScript.wasm` present).
- A published registry at `/opt/realm-rig/admin` (needs `appsettings.json`, see pitfalls) and `/opt/realm-rig/admin2`.
- Logs land in `/opt/realm-rig/logs/`; client and authority logs in
  `/root/.local/share/godot/app_userdata/{Realm,Realm.ListenServerHost}/logs/`.

### Rig commands

```bash
tools/rig/realm-rig.sh up          # six namespaces, routers with real MASQUERADE
tools/rig/realm-rig.sh status      # addresses per namespace
tools/rig/realm-rig.sh down
tools/rig/realm-rig.sh wrapper on  # installs the Linux authority launcher the client expects
tools/rig/realm-rig.sh wrapper off # removes it
```

Namespaces: `realm-host` (10.10.0.2 behind 203.0.113.2), `realm-player` (10.20.0.2 behind 198.51.100.2),
`realm-public` (203.0.113.1, runs the registry), `realm-host-router`, `realm-player-router`, `realm-admin`.

### The full run, in ONE shell invocation

```bash
wsl -d Ubuntu -u root -- bash -s <<'EOF' 2>&1 | tail -30
set -u
export PATH=$PATH:/usr/local/dotnet:/opt/dotnet
cd /mnt/d/git/Realm/Realm
GODOT=/opt/realm-rig/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64
C=/mnt/d/git/Realm/Realm/Realm.Client
SCENE=res://Testing/LobbyDirectConnectHarness.tscn
LOGS=/opt/realm-rig/logs; rm -f $LOGS/*.log $LOGS/*.done $LOGS/invite.txt

tools/rig/realm-rig.sh up >/dev/null
tools/rig/realm-rig.sh wrapper on >/dev/null

# Registry. It publishes for linux-x64; rebuild it after changing Realm.AdminServer.
cp /opt/realm-rig/admin/appsettings.template.json /opt/realm-rig/admin2/appsettings.json
( cd /opt/realm-rig/admin2 && exec ip netns exec realm-public dotnet Realm.AdminServer.dll --urls http://203.0.113.1:5000 > $LOGS/registry.log 2>&1 ) &
sleep 10
grep -c "Now listening" $LOGS/registry.log     # must be 1

# Host: a listed lobby behind its router, with nothing forwarded.
( ip netns exec realm-host "$GODOT" --headless --path $C $SCENE -- \
    --role=host --mode=listed --map=/opt/realm-rig/map \
    --registry=http://203.0.113.1:5000 \
    --invite-file=$LOGS/invite.txt --done-file=$LOGS/host.done --wait-seconds=120 \
    > $LOGS/host.log 2>&1 ) &
for i in $(seq 1 40); do sleep 4; [ -s $LOGS/invite.txt ] && break; done
LID=$(sed -n 2p $LOGS/invite.txt)              # line 1 is the port, line 2 the lobby id

# Joiner, behind its own router.
ip netns exec realm-player "$GODOT" --headless --path $C $SCENE -- \
    --role=guest --lobby=$LID --registry=http://203.0.113.1:5000 \
    --done-file=$LOGS/guest.done --wait-seconds=40 > $LOGS/guest.log 2>&1 &
for i in $(seq 1 22); do sleep 4; [ -f $LOGS/guest.done ] && break; done

echo "--- guest ---";    head -2 $LOGS/guest.done 2>/dev/null
echo "--- host ---";     grep -iE "registered|punch|peer .*(connected|dropped|left|refused)|seated" $LOGS/host.log | tail -8
echo "--- authority ---"; A=$(ls -t /root/.local/share/godot/app_userdata/Realm/logs/local_authority*.log | head -1); \
                          grep -iE "punch|peer|refus|lobby opened" "$A" | tail -8
pkill -f Realm.AdminServer 2>/dev/null; pkill -f "Godot_v4.7-stable_mono_linux" 2>/dev/null; true
EOF
```

**Harness exit codes:** `0` success, `2` hosting failed, `3` no guest arrived, `4` invite never written, `5` no port,
`6` listed join failed. Always read `guest.done` — it holds the sentence, not just the number.

### The router's own connection table

The most decisive instrument in the rig. Read it from the joiner's router while a join is in flight:

```bash
( for i in $(seq 1 10); do ip netns exec realm-player-router cat /proc/net/nf_conntrack \
      | grep "203.0.113.2" >> /opt/realm-rig/logs/ct.txt; sleep 5; done ) &
```

A working join shows one flow marked `[ASSURED]` with byte counts in both directions. Two `[UNREPLIED]` flows pointing
at each other mean the two ends disagree about the port. This is the acceptance check for the item in section 6 P0.

`conntrack` and `tcpdump` are not installed; `/proc/net/nf_conntrack` is readable and is enough.

### Pitfalls, each of which has cost this project hours

1. **A WSL restart reclaims every namespace.** A rig brought up in one command is gone by the next. Run a scenario
   start-to-finish inside a single invocation. Keep artefacts under `/opt/realm-rig`; `/tmp` does not survive.
2. **The harness scene must be passed explicitly.** `--path Realm.Client` alone runs the project's main scene, which
   is the main menu: hosting is never attempted and everything looks like a hang. Pass
   `res://Testing/LobbyDirectConnectHarness.tscn`.
3. **The registry needs `appsettings.json`.** The published folder ships only `appsettings.template.json`; without a copy
   under the real name it aborts with `StorageDirectory is required`. It also needs `--urls http://203.0.113.1:5000`.
4. **Rebuild the registry after changing it**, for example
   `dotnet publish Realm.AdminServer/Realm.AdminServer.csproj -c Release -r linux-x64 --self-contained false -o /opt/realm-rig/admin2`.
   The rig runs the published copy, not the source.
5. **Godot runs the Debug assembly.** Building `-c Release` does not affect what `--path` runs; use a plain
   `dotnet build` for the projects Godot hosts (`Realm.Client`, `Realm.ListenServerHost`).
6. **Clock offsets.** Authority logs are UTC; an `awk`-timestamped client log is local (seven hours behind here).
   Convert before comparing.
7. **The client's stdout is buffered when piped.** When a run seems to stop, read
   `…/app_userdata/Realm/logs/godot.log` and the authority log rather than only the redirected stdout.
8. **Never `pkill -f` on a pattern that could match your own command line** while a rig process is the target; kill by
   recorded pid where possible.

---

## 5. What the branch already contains (do not redo)

- Join handshake: the joiner's own-socket punch, the registry's report ticket, the host's punch toward the joiner, and
  the ticket lifetime fix (`8ed2d94`).
- LAN inference no longer treats two equal public addresses as a LAN (`A2`).
- One long join budget rather than a divided one (`A4`); no blocking bind-retry (`A5`).
- A stale public port does not survive a failed measurement (`A8`).
- The register route refuses a non-public endpoint (`A11`).
- A listed lobby publishes behind its room rather than blocking it.
- `join-lobby` diagnoses a join in phases with per-phase exit codes (CLI joiner).
- Bounded UPnP gateway discovery so hosting cannot hang (`122abb2`).
- Authority logging for every punch sent and every peer refused one (`f99c0f5`).
- The Linux authority launcher and its argument handling (`0bedde6`, `496447b`), and the harness's `--registry` and
  `--lobby` support (`b1ff7c8`, `62c98ae`).
- The namespace rig and `docs/rigs/README.md`.

---

## 6. The work, in order

Each item names its acceptance measurement. Do them in this order; later items depend on the port being right.

### P0 — Publish the port the transport is bound to (blocker)

The advertised endpoint must be the local port `CreateServer` actually holds, including the fallback path. Either
advertise the bound port, or refuse to fall back and fail hosting loudly — prefer the former.

*Acceptance:* in the rig, the joiner's conntrack shows one `[ASSURED]` flow instead of two `[UNREPLIED]` ones, the
host's authority logs the joiner's `ClientHello` and seats it, and the guest harness exits `0`.

### P1 — Finish the listed join end to end

If `P0` does not complete the join, continue with the state machine: the joiner must leave `Connecting`, receive
`HandshakeChallenge`, send `ClientHello`, receive `ServerHello` and `LobbyState`, and the host's roster must gain the
seat.

*Acceptance:* `guest.done` reads exit `0` with a seat count, and the host's harness reports the guest seated.

### P2 — Join-failure feedback to the host (workstream D)

When a join fails, the joiner reports the failure to the registry, the registry forwards it to the host's lobby socket,
and the host's lobby room shows it in chat.

*Acceptance:* a rig join with the host's port deliberately firewalled produces a chat line on the host naming the
joiner's failure, and one with a working path produces none.

### P3 — Honest reachability model (C1, C2, C5, C7, C8, C13 in that order)

Classify each published endpoint as Open, Punch-only or Unverified; add the cold probe; never refuse a Punch-only
lobby; publish the class; default the browser to hiding Unverified; then measure whether the router's mapping is reused
or per-destination by punching two different ports on the registry node and comparing observed source ports.

*Acceptance:* a forwarded host reads Open and a plain home router reads Punch-only; a host whose mapping is
per-destination is never told to forward a port; the advice text follows from the measurement.

### P4 — Diagnostics block and phase logs (I3, I7)

*Acceptance:* the host can copy a block of text that states port, bound port, measured address, class, mapping
behaviour, firewall state and the last join failure, and a join run logs its phases with timings.

### P5 — Port policy and addresses (B2, B3, B4, B5)

No hardcoded `8999` in user-facing text; no derived ports; only advertise a reachable seeder; every address shown or
copied carries its port.

*Acceptance:* hosting two instances on one machine collides nowhere, and a pasted invite always connects.

### P6 — Lobby UX and documentation (E1–E5, H1–H4)

*Acceptance:* the lobby room states in plain language what the host's network means for reachability and what to do,
and `docs/` contains a hosting guide that covers, with worked examples: a normal home router, a phone hotspot, a
VPN with port forwarding, and a shared overlay network such as Tailscale, stating for each whether hosting is possible,
what is required, and what will not work.

### P7 — Relay (F1–F9), only if P3's measurement says it is needed

Opt-in per screen, off by default, never the default path for a match. A relay carries packets, never simulation.

*Acceptance:* with the relay enabled on one host, a joiner that cannot reach it directly is admitted through the relay,
and with the relay disabled the same join fails with a sentence that names the situation.

### P8 — IPv6 (see `docs/ipv6_task.md`)

Not started. Do it last, and only after P0–P6 are green.

### P9 — Reconcile the checklist

`docs/network_host_resilience_task.md` shows 2 of 76 items ticked, which no longer reflects the branch. Update it
against the code as you go, so it can be used for planning again.

---

## 7. Definition of done for the branch

All of the following, measured on this checkout:

1. Rig: listed host behind a NAT with nothing forwarded publishes, a joiner finds and joins it, guest exits `0`.
2. Rig: the same scenario with the host's game port firewalled fails with a host-visible explanation, not a timeout.
3. Rig: a direct-connect guest still joins, and a loopback guest still joins.
4. Rig: two clients behind one public address (same-namespace or a shared router) are not mistaken for a LAN.
   Measured both ways: `listed-join.sh --same-machine` joins over the host's own network address in 144 ms, and
   `--lookalike-lan` (two networks that number themselves the same, different public addresses) disproves the near
   address in one short attempt and joins at the public one.
5. `dotnet build` clean for `Realm.Client`, `Realm.ListenServerHost`, `Realm.AdminServer`, `Realm.Shared`,
   `Realm.MapAPI`.
6. `Realm.Network.Tests` green, including the A10 state-machine coverage if it was written.
7. `docs/network_host_resilience_task.md` reconciled, `docs/rigs/README.md` current, hosting guide written.
8. No behaviour change for a map or mode that did not ask for one: the generic-API rules in `AGENTS.md` still hold.

## 8. What still needs a human

One thing only: the **two-machine matrix** — two real computers, one on a hotspot, one on a VPN with a different exit —
because the rig cannot model a real ISP, a real router's firmware, or a real VPN exit. Everything else in this document
can be completed and measured here.

## 9. Report shape

When you stop, report in this form, and only in this form:

- **Completed**, item by item, each with the measurement that proves it (log line, exit code, conntrack state).
- **Changed**, per file, one sentence each.
- **Blocked**, with the measurement that blocked it and what would unblock it.
- **Found but not fixed**, anything discovered along the way that is out of scope.
- **Left for the human**, with the exact steps to run.
