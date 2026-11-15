#!/usr/bin/env bash
# A lobby judged by two registry nodes instead of one. The host's router carries the punch a host has to send and loses the
# second one, so a single node can measure the outside port but never prove that every destination gets the same one: it
# refuses rather than list a port it cannot vouch for. A second node is the second destination, the host punches its probe
# socket too, and its agreement is what lets the lobby be published.
#
#   second-vantage.sh [--mapping reuse|translating|per-destination] [--peers 0|1]
#
#   --mapping reuse|translating|per-destination
#                           the host router's NAT mapping mode (see realm-rig.sh mapping)
#   --peers 0|1             whether the deployment has a second node (default 1). With 0 the same rig refuses the lobby,
#                           which is what shows the second node is what made the difference
#
# Exit: 0 the lobby was listed and a guest joined it; 4 the host refused to publish; 6 the guest could not join.
set -u
export PATH=${PATH}:/usr/local/dotnet:/opt/dotnet

REPO=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
GODOT=${GODOT:-/opt/realm-rig/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64}
CLIENT=$REPO/Realm.Client
SCENE=res://Testing/LobbyDirectConnectHarness.tscn
LOGS=${LOGS:-/opt/realm-rig/logs}
REGISTRY_DIR=${REGISTRY_DIR:-/opt/realm-rig/admin2}
FIRST_URL=http://203.0.113.1:5000
SECOND_URL=http://203.0.113.1:5001
PROBE_PORT=8998
SECOND_PROBE_PORT=8997
MAP=${MAP:-/opt/realm-rig/map}
MODE=reuse
PEERS=1

while [[ $# -gt 0 ]]; do
	case $1 in
		--mapping) MODE=$2; shift 2 ;;
		--peers) PEERS=$2; shift 2 ;;
		*) echo "unknown argument: $1" >&2; exit 1 ;;
	esac
done

mkdir -p "$LOGS"
rm -f "$LOGS"/*.log "$LOGS"/*.done "$LOGS"/invite.txt

"$REPO/tools/rig/realm-rig.sh" mapping "$MODE" >/dev/null

# The forwarding is off on purpose: this is the lobby a home router publishes without anybody configuring anything, which is
# the case the whole ticket is about.
REALM_RIG_BLOCK_SECOND_PUNCH=1 REALM_RIG_PROBE_PORT=$PROBE_PORT REALM_RIG_SECOND_PROBE_PORT=$SECOND_PROBE_PORT \
	"$REPO/tools/rig/realm-rig.sh" up >/dev/null
"$REPO/tools/rig/realm-rig.sh" wrapper on >/dev/null

# The router translates the game port's outside port and forwards nothing, which is a home router with a port mapping for
# traffic it started and no inbound rule: the rig's stable mapping installs the inbound translation so that the plain
# punch check can be measured, and this scenario is the one where it is not there.
OUTSIDE_PORT=$([[ $MODE == translating ]] && echo "${REALM_RIG_TRANSLATED_GAME_PORT:-10000}" || echo 8999)
ip netns exec realm-host-router iptables -t nat -D PREROUTING -i wan-host -p udp -d 203.0.113.2 --dport "$OUTSIDE_PORT" -j DNAT --to-destination "10.10.0.2:${REALM_RIG_GAME_PORT:-8999}"
echo "the host's router translates the game port to $OUTSIDE_PORT and forwards nothing to it"

cp "$REGISTRY_DIR/appsettings.template.json" "$REGISTRY_DIR/appsettings.json"
start_node() {
	local url=$1 probe=$2 storage=$3 peers=$4 log=$5
	( cd "$REGISTRY_DIR" && exec ip netns exec realm-public env \
		ReachabilityProbePort="$probe" ReachabilityProbePublicIp=203.0.113.1 \
		StorageDirectory=".data/$storage" SelfUrl="$url" Peers="$peers" \
		dotnet Realm.AdminServer.dll --urls "$url" > "$LOGS/$log" 2>&1 ) &
}

PEER_SETTING=""
[[ $PEERS -eq 1 ]] && PEER_SETTING=$SECOND_URL
start_node "$FIRST_URL" "$PROBE_PORT" cas-first "$PEER_SETTING" first-node.log
FIRST_PID=$!
if [[ $PEERS -eq 1 ]]; then
	start_node "$SECOND_URL" "$SECOND_PROBE_PORT" cas-second "$FIRST_URL" second-node.log
	SECOND_PID=$!
fi

for _ in $(seq 1 30); do
	sleep 1
	grep -q "Now listening" "$LOGS"/first-node.log 2>/dev/null && break
done
if ! grep -q "Now listening" "$LOGS"/first-node.log 2>/dev/null; then
	echo "the first node did not start; see $LOGS/first-node.log" >&2
	exit 1
fi
if [[ $PEERS -eq 1 ]]; then
	for _ in $(seq 1 30); do
		sleep 1
		grep -q "Now listening" "$LOGS"/second-node.log 2>/dev/null && break
	done
	if ! grep -q "Now listening" "$LOGS"/second-node.log 2>/dev/null; then
		echo "the second node did not start; see $LOGS/second-node.log" >&2
		exit 1
	fi
fi

( ip netns exec realm-host "$GODOT" --headless --path "$CLIENT" "$SCENE" -- \
	--role=host --mode=listed --map="$MAP" --registry="$FIRST_URL" \
	--invite-file="$LOGS"/invite.txt --done-file="$LOGS"/host.done --wait-seconds=90 \
	2>&1 | while IFS= read -r line; do printf '%s %s\n' "$(date -u +%H:%M:%S)" "$line"; done > "$LOGS"/host.log ) &
HOST_PID=$!

for _ in $(seq 1 40); do
	sleep 4
	[[ -s $LOGS/invite.txt ]] && break
done

echo "--- the host's reachability lines"
grep -E "rehearsing|reached this lobby|other node|Measuring|Refus|unmeasured|listed|not published" "$LOGS"/host.log | tail -8
echo "--- what the first node measured"
grep -E "other node|Checking 203.0.113.2" "$LOGS"/first-node.log | tail -4
echo "--- what the second node measured"
[[ -f $LOGS/second-node.log ]] && grep -E "Checking 203.0.113.2" "$LOGS"/second-node.log | tail -3

if [[ ! -s $LOGS/invite.txt ]]; then
	echo "the host never published; the deployment had $PEERS node(s)"
	kill "$FIRST_PID" ${SECOND_PID:-} 2>/dev/null
	pkill -f "Godot_v4.7-stable_mono_linux" 2>/dev/null
	exit 4
fi

LOBBY_ID=$(sed -n 2p "$LOGS"/invite.txt)
echo "--- the registry lists the lobby as $(ip netns exec realm-public curl -s "$FIRST_URL/lobbies" | grep -o -i '"reachabilityclass":"[A-Za-z]*"' | head -1)"

( ip netns exec realm-player "$GODOT" --headless --path "$CLIENT" "$SCENE" -- \
	--role=guest --lobby="$LOBBY_ID" --registry="$FIRST_URL" \
	--done-file="$LOGS"/guest.done --wait-seconds=40 > "$LOGS"/guest.log 2>&1 ) &
GUEST_PID=$!
for _ in $(seq 1 24); do
	sleep 4
	[[ -f $LOGS/guest.done ]] && break
done

echo "--- guest"
head -3 "$LOGS"/guest.done 2>/dev/null || echo "no guest result"

kill "$GUEST_PID" "$HOST_PID" "$FIRST_PID" ${SECOND_PID:-} 2>/dev/null
pkill -f "Godot_v4.7-stable_mono_linux" 2>/dev/null
wait 2>/dev/null

GUEST_EXIT=$(sed -n 1p "$LOGS"/guest.done 2>/dev/null)
[[ ${GUEST_EXIT:-} == "0" ]] && exit 0
exit "${GUEST_EXIT:-6}"
