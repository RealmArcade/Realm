#!/usr/bin/env bash
# A listed-lobby join on one machine: a host behind a router with nothing forwarded publishes, and a joiner behind its
# own router finds the lobby by identifier and is admitted. Both routers' connection tables are sampled while the join
# is in flight, which is the instrument that tells a punch that worked from two ends that disagree about a port.
#
#   listed-join.sh [--mapping reuse|translating|per-destination] [--forward] [--registry-only]
#
#   --mapping reuse|translating|per-destination
#                           the routers' NAT mapping mode (see realm-rig.sh mapping). `translating` is a router that keeps
#                           one mapping for every destination but gives it an outside port of its own, which is the shape
#                           almost every home router presents and the one a lobby has to be listed at correctly.
#   --forward               open the host's game port to unsolicited traffic on its router, which is the case a player who
#                           did the work gets
#   --registry-only         start the registry and stop, so a scenario can be driven by hand
#   --block-punch           drop the punches the host's game port sends toward the player, which is the network where a
#                           listed lobby is published and nobody can get in
#   --fail-storm N          after the guest gives up, post N join-failure reports from the player's network at N distinct
#                           endpoints, which is how the lobby's line limit and its downgrade are measured
#   --same-machine          run the guest in the host's own namespace, so both screens share one public address. This is
#                           the same-namespace variant of "two players behind one public address", where the host's own
#                           network address is genuinely reachable and the join is expected to stay on it.
#   --lookalike-lan         put the player's machine on an address inside the host's own subnet numbers, which is the
#                           office NAT, hotel NAT or VPN exit where two separate networks both call themselves 10.10.0.0/24
#                           and the host's private address is one the player can see but cannot reach
set -u
export PATH=${PATH}:/usr/local/dotnet:/opt/dotnet

REPO=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
GODOT=${GODOT:-/opt/realm-rig/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64}
CLIENT=$REPO/Realm.Client
SCENE=res://Testing/LobbyDirectConnectHarness.tscn
LOGS=${LOGS:-/opt/realm-rig/logs}
REGISTRY_DIR=${REGISTRY_DIR:-/opt/realm-rig/admin2}
REGISTRY_URL=http://203.0.113.1:5000
MAP=${MAP:-/opt/realm-rig/map}
MODE=reuse
FORWARD=0
REGISTRY_ONLY=0
BLOCK_PUNCH=0
FAIL_STORM=0
LOOKALIKE_LAN=0
SAME_MACHINE=0
GUEST_NS=realm-player

while [[ $# -gt 0 ]]; do
	case $1 in
		--mapping) MODE=$2; shift 2 ;;
		--forward) FORWARD=1; shift ;;
		--block-punch) BLOCK_PUNCH=1; shift ;;
		--fail-storm) FAIL_STORM=$2; shift 2 ;;
		--lookalike-lan) LOOKALIKE_LAN=1; shift ;;
		--same-machine) SAME_MACHINE=1; GUEST_NS=realm-host; shift ;;
		--registry-only) REGISTRY_ONLY=1; shift ;;
		*) echo "unknown argument: $1" >&2; exit 1 ;;
	esac
done

mkdir -p "$LOGS"
rm -f "$LOGS"/*.log "$LOGS"/*.done "$LOGS"/invite.txt "$LOGS"/ct-*.txt

"$REPO/tools/rig/realm-rig.sh" mapping "$MODE" >/dev/null
REALM_RIG_FORWARD_GAME_PORT=$FORWARD "$REPO/tools/rig/realm-rig.sh" up >/dev/null
"$REPO/tools/rig/realm-rig.sh" wrapper on >/dev/null

if [[ $FORWARD -eq 1 ]]; then
	echo "the host's router now forwards UDP 8999 to 10.10.0.2:8999, unsolicited traffic included"
fi

if [[ $BLOCK_PUNCH -eq 1 ]]; then
	ip netns exec realm-host-router iptables -I FORWARD 1 -i lan-host -o wan-host -p udp -s 10.10.0.2 --sport 8999 -d 198.51.100.0/24 -j DROP
	echo "the host's router now drops every punch its game port sends toward the player's network"
fi

if [[ $LOOKALIKE_LAN -eq 1 ]]; then
	ip netns exec realm-player ip addr add 10.10.0.99/24 dev eth0
	ip netns exec realm-player ip route add blackhole 10.10.0.2/32
	echo "the player's machine now also carries 10.10.0.99/24, so the host's 10.10.0.2 looks like a neighbour it cannot reach"
fi

cp "$REGISTRY_DIR/appsettings.template.json" "$REGISTRY_DIR/appsettings.json"
( cd "$REGISTRY_DIR" && exec ip netns exec realm-public dotnet Realm.AdminServer.dll --urls "$REGISTRY_URL" > "$LOGS"/registry.log 2>&1 ) &
REGISTRY_PID=$!
for _ in $(seq 1 30); do
	sleep 1
	grep -q "Now listening" "$LOGS"/registry.log 2>/dev/null && break
done
if ! grep -q "Now listening" "$LOGS"/registry.log 2>/dev/null; then
	echo "the registry did not start; see $LOGS/registry.log" >&2
	exit 1
fi
if [[ $REGISTRY_ONLY -eq 1 ]]; then
	echo "registry up at $REGISTRY_URL (pid $REGISTRY_PID)"
	exit 0
fi

( ip netns exec realm-host "$GODOT" --headless --path "$CLIENT" "$SCENE" -- \
	--role=host --mode=listed --map="$MAP" --registry="$REGISTRY_URL" \
	--invite-file="$LOGS"/invite.txt --done-file="$LOGS"/host.done --wait-seconds=120 \
	> "$LOGS"/host.log 2>&1 ) &
HOST_PID=$!

for _ in $(seq 1 40); do
	sleep 4
	[[ -s $LOGS/invite.txt ]] && break
done
if [[ ! -s $LOGS/invite.txt ]]; then
	echo "the host never wrote its invite; see $LOGS/host.log" >&2
	pkill -f "realm-host" 2>/dev/null
	kill "$REGISTRY_PID" 2>/dev/null
	exit 4
fi
LOBBY_ID=$(sed -n 2p "$LOGS"/invite.txt)
LOBBY_PORT=$(sed -n 1p "$LOGS"/invite.txt)
echo "host published lobby $LOBBY_ID on port $LOBBY_PORT"

( for _ in $(seq 1 12); do
	ip netns exec realm-player-router grep udp /proc/net/nf_conntrack 2>/dev/null >> "$LOGS"/ct-player.txt
	ip netns exec realm-host-router grep udp /proc/net/nf_conntrack 2>/dev/null >> "$LOGS"/ct-host.txt
	ip netns exec realm-host ss -uap 2>/dev/null >> "$LOGS"/sockets-host.txt
	sleep 4
done ) &

( ip netns exec "$GUEST_NS" "$GODOT" --headless --path "$CLIENT" "$SCENE" -- \
	--role=guest --lobby="$LOBBY_ID" --registry="$REGISTRY_URL" \
	--done-file="$LOGS"/guest.done --wait-seconds=40 \
	> "$LOGS"/guest.log 2>&1 ) &
GUEST_PID=$!

for _ in $(seq 1 24); do
	sleep 4
	[[ -f $LOGS/guest.done ]] && break
done

sleep 2
echo "--- guest ---"
head -3 "$LOGS"/guest.done 2>/dev/null || echo "no guest result"
echo "--- host ---"
grep -iE "reach|registered|public=|punch|peer|seat" "$LOGS"/host.log | tail -12
echo "--- authority ---"
AUTHORITY_LOG=$(ls -t /root/.local/share/godot/app_userdata/Realm/logs/local_authority*.log 2>/dev/null | head -1)
[[ -n ${AUTHORITY_LOG:-} ]] && grep -iE "punch|peer|refus|lobby opened|seat" "$AUTHORITY_LOG" | tail -12
echo "--- player router conntrack (duplicates collapsed) ---"
sort -u "$LOGS"/ct-player.txt 2>/dev/null | tail -8
if [[ $FAIL_STORM -gt 0 ]]; then
	for index in $(seq 1 "$FAIL_STORM"); do
		body="{\"LobbyId\":\"$LOBBY_ID\",\"Phase\":\"answer\",\"Endpoint\":\"198.51.100.9:6$index\",\"Attempts\":2,\"ElapsedMilliseconds\":30000}"
		status=$(ip netns exec realm-player curl -s -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -d "$body" "$REGISTRY_URL/lobbies/join-failure")
		echo "report $index: $status"
	done
	echo "--- lobby lines the host was given ---"
	[[ -n ${AUTHORITY_LOG:-} ]] && grep -F "system chat" "$AUTHORITY_LOG" | tail -8
fi

echo "--- host router conntrack (duplicates collapsed) ---"
sort -u "$LOGS"/ct-host.txt 2>/dev/null | tail -8

kill "$GUEST_PID" "$HOST_PID" 2>/dev/null
pkill -f "Godot_v4.7-stable_mono_linux" 2>/dev/null
kill "$REGISTRY_PID" 2>/dev/null
wait 2>/dev/null

GUEST_EXIT=$(sed -n 1p "$LOGS"/guest.done 2>/dev/null)
[[ ${GUEST_EXIT:-} == "0" ]] && exit 0
exit "${GUEST_EXIT:-3}"
