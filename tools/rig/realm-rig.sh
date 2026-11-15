#!/usr/bin/env bash
# The single-machine network rig: two hosts behind their own routers, on an "internet" made of a third namespace.
#
# Everything here is a namespace, so nothing is installed on the machine that runs it and no desktop hypervisor is
# involved. The addresses are from the documentation range (203.0.113.0/24, TEST-NET-3) rather than from a private one:
# they are globally scoped, so a lobby server on this rig judges a host the way a public node does and the
# "never judge a caller on your own network" rule is exercised rather than bypassed. Nothing leaves the machine because
# no route exists to anywhere else.
#
#   realm-rig.sh up             build the namespaces and their NATs
#   realm-rig.sh down           remove them
#   realm-rig.sh status         print each namespace's addresses and NAT mode
#   realm-rig.sh mapping reuse|translating|per-destination
#                               choose whether a router gives the game port one stable external port for every destination
#                               (reuse, which is what a home router does) or makes a new one per destination
#                               (per-destination, which is the carrier-grade and VPN case no punch can rescue)
#   realm-rig.sh host <cmd...>  run a command inside the host's namespace
#   realm-rig.sh player <cmd...>    run a command inside the player's namespace
#   realm-rig.sh public <cmd...>    run a command inside the registry namespace
set -euo pipefail

# Two separate /30 segments rather than one /24: an "internet" namespace with both routers on one subnet would answer
# either router's address resolution on whichever interface asked first and route the reply out the wrong one.
REGISTRY_IP=203.0.113.1
HOST_WAN_PUBLIC=203.0.113.1
HOST_ROUTER=203.0.113.2
PLAYER_WAN_PUBLIC=198.51.100.1
PLAYER_ROUTER=198.51.100.2
HOST_LAN=10.10.0
PLAYER_LAN=10.20.0

PUBLIC_NS=realm-public
HOST_ROUTER_NS=realm-host-router
PLAYER_ROUTER_NS=realm-player-router
HOST_NS=realm-host
PLAYER_NS=realm-player
MODE_FILE=/tmp/realm-rig-mapping
GAME_PORT=${REALM_RIG_GAME_PORT:-8999}
TRANSLATED_GAME_PORT=${REALM_RIG_TRANSLATED_GAME_PORT:-10000}

require_root() {
	if [[ $(id -u) -ne 0 ]]; then
		echo "run this as root: it creates namespaces and network interfaces" >&2
		exit 1
	fi
}

exists() {
	ip netns list | grep -qx "$1"
}

teardown() {
	for ns in "$HOST_NS" "$PLAYER_NS" "$HOST_ROUTER_NS" "$PLAYER_ROUTER_NS" "$PUBLIC_NS"; do
		if exists "$ns"; then ip netns del "$ns"; fi
	done
}

link_pair() {
	local left_ns=$1 left_name=$2 left_ip=$3
	local right_ns=$4 right_name=$5 right_ip=$6
	local prefix=$7

	ip link add "$left_name" netns "$left_ns" type veth peer name "$right_name" netns "$right_ns"
	ip -n "$left_ns" addr add "${left_ip}/${prefix}" dev "$left_name"
	ip -n "$left_ns" link set "$left_name" up
	ip -n "$right_ns" addr add "${right_ip}/${prefix}" dev "$right_name"
	ip -n "$right_ns" link set "$right_name" up
}

configure_router() {
	local ns=$1 wan=$2 lan=$3 nat_argument=$4

	ip -n "$ns" link set lo up
	ip netns exec "$ns" sysctl -qw net.ipv4.ip_forward=1
	ip netns exec "$ns" iptables -t nat -A POSTROUTING -o "$wan" -j MASQUERADE $nat_argument

	# The policy is DROP rather than the chain's default ACCEPT, because a forwarded packet that no rule names is exactly
	# the unsolicited traffic a router drops: without this every inbound packet the mapping translated would be delivered
	# and the rig would report an open game port on a router that never opened one.
	ip netns exec "$ns" iptables -P FORWARD DROP
	ip netns exec "$ns" iptables -A FORWARD -i "$lan" -o "$wan" -j ACCEPT
	ip netns exec "$ns" iptables -A FORWARD -i "$wan" -o "$lan" -m conntrack --ctstate RELATED,ESTABLISHED -j ACCEPT
}

# A real home router keeps one mapping per internal port, so the game port answers from the same external port whatever it
# is talking to, and it drops unsolicited traffic until the machine behind it has sent toward the sender. Linux netfilter
# does neither by itself: an unsolicited inbound packet creates connection state that then takes the external port the
# machine's own next flow would have been given, and its mapping is per destination. A static mapping for the game port,
# with the forward that only lets related traffic through, is the same arrangement a router with a fixed mapping and a
# filtered outside face presents to the internet, which is the case a punch exists for. Without this the rig is harsher than
# any router a player owns: two ends behind it disagree about the port on every join, and the punch is blamed for the
# simulator's own bookkeeping.
#
# The outside port is a parameter because a router picks it: `reuse` keeps the port the game holds, and `translating` is
# the same mapping on an outside port of the router's own choosing, which is what almost every home router does and what a
# UPnP mapping that was granted on another port produces.
configure_stable_mapping() {
	local ns=$1 wan=$2 lan_ip=$3 wan_ip=$4 port=$5 outside_port=${6:-$5}

	ip netns exec "$ns" iptables -t nat -I POSTROUTING 1 -o "$wan" -p udp -s "$lan_ip" --sport "$port" -j SNAT --to-source "${wan_ip}:${outside_port}"
	ip netns exec "$ns" iptables -t nat -A PREROUTING -i "$wan" -p udp -d "$wan_ip" --dport "$outside_port" -j DNAT --to-destination "${lan_ip}:${port}"
}

forward_game_port() {
	local ns=$1 wan=$2 lan=$3 port=$4

	ip netns exec "$ns" iptables -I FORWARD 1 -i "$wan" -o "$lan" -p udp --dport "$port" -m conntrack --ctstate NEW,RELATED,ESTABLISHED -j ACCEPT
}

up() {
	require_root
	teardown

	ip netns add "$PUBLIC_NS"
	ip netns add "$HOST_ROUTER_NS"
	ip netns add "$PLAYER_ROUTER_NS"
	ip netns add "$HOST_NS"
	ip netns add "$PLAYER_NS"

	ip -n "$PUBLIC_NS" link set lo up

	# The internet namespace routes between the two routers' segments, so without this the two machines cannot reach each
	# other at all and every "blocked by NAT" result would really be "blocked by this namespace".
	ip netns exec "$PUBLIC_NS" sysctl -qw net.ipv4.ip_forward=1

	# The registry's namespace is the internet: both routers are directly attached to it, which is what lets the
	# reachability check see a host arrive from a globally scoped address.
	link_pair "$PUBLIC_NS" pub-host "$HOST_WAN_PUBLIC" "$HOST_ROUTER_NS" wan-host "$HOST_ROUTER" 30
	link_pair "$PUBLIC_NS" pub-player "$PLAYER_WAN_PUBLIC" "$PLAYER_ROUTER_NS" wan-player "$PLAYER_ROUTER" 30

	link_pair "$HOST_ROUTER_NS" lan-host "${HOST_LAN}.1" "$HOST_NS" eth0 "${HOST_LAN}.2" 24
	link_pair "$PLAYER_ROUTER_NS" lan-player "${PLAYER_LAN}.1" "$PLAYER_NS" eth0 "${PLAYER_LAN}.2" 24

	local mapping_mode=reuse
	if [[ -f $MODE_FILE ]]; then mapping_mode=$(cat "$MODE_FILE"); fi
	if [[ $mapping_mode != reuse && $mapping_mode != translating && $mapping_mode != per-destination ]]; then mapping_mode=reuse; fi

	local nat_argument=""
	if [[ $mapping_mode == per-destination ]]; then nat_argument="--random"; fi

	configure_router "$HOST_ROUTER_NS" wan-host lan-host "$nat_argument"
	configure_router "$PLAYER_ROUTER_NS" wan-player lan-player "$nat_argument"

	if [[ $mapping_mode == reuse ]]; then
		configure_stable_mapping "$HOST_ROUTER_NS" wan-host "${HOST_LAN}.2" "$HOST_ROUTER" "$GAME_PORT"
	fi
	if [[ $mapping_mode == translating ]]; then
		configure_stable_mapping "$HOST_ROUTER_NS" wan-host "${HOST_LAN}.2" "$HOST_ROUTER" "$GAME_PORT" "$TRANSLATED_GAME_PORT"
	fi
	if [[ ${REALM_RIG_FORWARD_GAME_PORT:-0} == 1 ]]; then
		forward_game_port "$HOST_ROUTER_NS" wan-host lan-host "$GAME_PORT"
	fi

	for ns in "$HOST_NS" "$PLAYER_NS"; do
		ip -n "$ns" link set lo up
	done
	ip -n "$HOST_NS" route add default via "${HOST_LAN}.1"
	ip -n "$PLAYER_NS" route add default via "${PLAYER_LAN}.1"

	ip -n "$HOST_ROUTER_NS" route add default via "$HOST_WAN_PUBLIC"
	ip -n "$PLAYER_ROUTER_NS" route add default via "$PLAYER_WAN_PUBLIC"

	echo "rig up: registry ${REGISTRY_IP} (and ${PLAYER_WAN_PUBLIC}), host ${HOST_LAN}.2 behind ${HOST_ROUTER}, player ${PLAYER_LAN}.2 behind ${PLAYER_ROUTER}"
}

down() {
	require_root
	teardown
	echo "rig down"
}

status() {
	ip netns list | grep '^realm-' || echo "no rig namespaces"
	echo "mapping mode: $(cat "$MODE_FILE" 2>/dev/null || echo reuse)"
	for ns in "$PUBLIC_NS" "$HOST_ROUTER_NS" "$PLAYER_ROUTER_NS" "$HOST_NS" "$PLAYER_NS"; do
		if exists "$ns"; then
			echo "-- $ns"
			ip -n "$ns" -br addr show | sed 's/^/   /'
		fi
	done
}

mapping() {
	require_root
	local mode=${1:-reuse}
	if [[ $mode == random ]]; then mode=per-destination; fi
	if [[ $mode != reuse && $mode != translating && $mode != per-destination ]]; then
		echo "mapping takes 'reuse', 'translating' or 'per-destination'" >&2
		exit 1
	fi

	echo "$mode" > "$MODE_FILE"
	if exists "$HOST_ROUTER_NS"; then
		up
	fi
}

run_in() {
	local ns=$1
	shift
	if ! exists "$ns"; then
		echo "the rig is not up; run '$0 up'" >&2
		exit 1
	fi

	ip netns exec "$ns" "$@"
}

wrapper() {
	local project_dir
	project_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")/../../Realm.ListenServerHost" && pwd)
	local launcher="$project_dir/Realm.ListenServerHost"

	shift || true
	case "${1:-on}" in
		on)
			cp "$(dirname "${BASH_SOURCE[0]}")/authority-wrapper.sh" "$launcher"
			chmod +x "$launcher"
			echo "the client will launch the authority through $launcher"
			;;
		off)
			rm -f "$launcher"
			echo "removed $launcher"
			;;
		*)
			echo "wrapper takes 'on' or 'off'" >&2
			exit 1
			;;
	esac
}

case "${1:-}" in
	wrapper) wrapper "$@" ;;
	up) up ;;
	down) down ;;
	status) status ;;
	mapping) shift; mapping "${1:-}" ;;
	host) shift; run_in "$HOST_NS" "$@" ;;
	player) shift; run_in "$PLAYER_NS" "$@" ;;
	public) shift; run_in "$PUBLIC_NS" "$@" ;;
	*)
		echo "usage: $0 up|down|status|wrapper on|off|mapping reuse|translating|per-destination|host <cmd>|player <cmd>|public <cmd>" >&2
		exit 1
		;;
esac
