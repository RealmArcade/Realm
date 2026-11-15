#!/usr/bin/env bash
# A listed lobby whose router stops forwarding. The lobby is published through a router that forwards its game port, the
# forwarding is then withdrawn while the host keeps playing, and the lobby has to stop saying Open within the minute it
# promised. What is withdrawn is the forward and the destination translation: the host's own outbound mapping is left
# alone, because a router that keeps a mapping alive with the traffic it already has is exactly the router that stops
# admitting strangers while every player already punched keeps playing.
#
#   reachability-withdrawal.sh [--mapping reuse|translating] [--wait 75]
#
#   --mapping reuse|translating
#                           the host router's NAT mapping mode (see realm-rig.sh mapping); both keep the forwarding the
#                           withdrawal takes away
#   --wait N                how many seconds to keep watching for the downgrade (default 75; the acceptance is 60)
#
# Exit: 0 the listing moved down within the wait, and the registry states the weaker class; 4 the host never published;
#       5 the listing still claims Open after the wait.
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
HOST_ADDRESS=203.0.113.2
HOST_MACHINE=10.10.0.2
MAPPING_PORT=8999
WAIT_SECONDS=75

while [[ $# -gt 0 ]]; do
	case $1 in
		--mapping) MAPPING_PORT=$([[ $2 == translating ]] && echo "${REALM_RIG_TRANSLATED_GAME_PORT:-10000}" || echo 8999); MODE=$2; shift 2 ;;
		--wait) WAIT_SECONDS=$2; shift 2 ;;
		*) echo "unknown argument: $1" >&2; exit 1 ;;
	esac
done

listed_class() {
	ip netns exec realm-public curl -s "$REGISTRY_URL/lobbies" 2>/dev/null | grep -o -i '"reachabilityclass":"[A-Za-z]*"' | head -1 | sed 's/.*:"//; s/"$//'
}

mkdir -p "$LOGS"
rm -f "$LOGS"/host.log "$LOGS"/host.done "$LOGS"/invite.txt "$LOGS"/registry.log

"$REPO/tools/rig/realm-rig.sh" mapping "${MODE:-reuse}" >/dev/null
REALM_RIG_FORWARD_GAME_PORT=1 "$REPO/tools/rig/realm-rig.sh" up >/dev/null
"$REPO/tools/rig/realm-rig.sh" wrapper on >/dev/null

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

( ip netns exec realm-host "$GODOT" --headless --path "$CLIENT" "$SCENE" -- \
	--role=host --mode=listed --map="$MAP" --registry="$REGISTRY_URL" \
	--invite-file="$LOGS"/invite.txt --done-file="$LOGS"/host.done --wait-seconds=$((WAIT_SECONDS + 60)) \
	2>&1 | while IFS= read -r line; do printf '%s %s\n' "$(date -u +%H:%M:%S)" "$line"; done > "$LOGS"/host.log ) &
HOST_PID=$!

for _ in $(seq 1 40); do
	sleep 4
	[[ -s $LOGS/invite.txt ]] && break
done
if [[ ! -s $LOGS/invite.txt ]]; then
	echo "the host never published; see $LOGS/host.log" >&2
	pkill -f "realm-host" 2>/dev/null
	kill "$REGISTRY_PID" 2>/dev/null
	exit 4
fi
LOBBY_PORT=$(sed -n 1p "$LOGS"/invite.txt)
PUBLISHED=""
for _ in $(seq 1 12); do
	sleep 5
	PUBLISHED=$(listed_class)
	[[ -n $PUBLISHED ]] && break
done
if [[ -z $PUBLISHED ]]; then
	echo "the host published but the registry never listed it; see $LOGS/host.log" >&2
	pkill -f "realm-host" 2>/dev/null
	kill "$REGISTRY_PID" 2>/dev/null
	exit 4
fi
echo "host published on port $LOBBY_PORT and the registry lists it as $PUBLISHED"

ip netns exec realm-host-router iptables -D FORWARD -i wan-host -o lan-host -p udp --dport "$LOBBY_PORT" -m conntrack --ctstate NEW,RELATED,ESTABLISHED -j ACCEPT
ip netns exec realm-host-router iptables -t nat -D PREROUTING -i wan-host -p udp -d "$HOST_ADDRESS" --dport "$MAPPING_PORT" -j DNAT --to-destination "${HOST_MACHINE}:${LOBBY_PORT}"
WITHDRAWN_AT=$(date -u +%s)
echo "forwarding withdrawn at $(date -u +%H:%M:%S):"
echo "  forward rules now: $(ip netns exec realm-host-router iptables -S FORWARD | tr '\n' ' ')"
echo "  destination translations now: $(ip netns exec realm-host-router iptables -t nat -S PREROUTING | tr '\n' ' ')"

CURRENT=$PUBLISHED
while [[ $(date -u +%s) -lt $((WITHDRAWN_AT + WAIT_SECONDS)) ]]; do
	sleep 5
	CURRENT=$(listed_class)
	[[ -n $CURRENT && $CURRENT != "$PUBLISHED" ]] && break
done
ELAPSED=$(( $(date -u +%s) - WITHDRAWN_AT ))

echo "--- the host's view:"
grep -E "Measuring this lobby again|still |no longer listed|re-checks" "$LOGS"/host.log | tail -4
echo "--- the listing states ${CURRENT:-nothing} after ${ELAPSED}s (published as ${PUBLISHED:-unknown})"
echo "--- a stranger's first datagram now:"
ip netns exec realm-public python3 "$REPO/tools/rig/reachability-stranger-probe.py" "$HOST_ADDRESS" "$MAPPING_PORT"

kill "$HOST_PID" 2>/dev/null
pkill -f "Godot_v4.7-stable_mono_linux" 2>/dev/null
kill "$REGISTRY_PID" 2>/dev/null
wait 2>/dev/null

if [[ -z $PUBLISHED || $CURRENT == "$PUBLISHED" ]]; then
	echo "the lobby still claims ${PUBLISHED:-nothing} ${ELAPSED}s after its forwarding was withdrawn" >&2
	exit 5
fi
if [[ $ELAPSED -gt $WAIT_SECONDS ]]; then
	echo "the lobby was only moved down after ${ELAPSED}s, which is longer than the minute it promises" >&2
	exit 5
fi
echo "the lobby stopped claiming ${PUBLISHED} after ${ELAPSED}s and is listed as ${CURRENT}"
exit 0
