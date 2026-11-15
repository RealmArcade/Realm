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
#   realm-rig.sh mapping reuse|random
#                               choose whether a router keeps one external port per destination (reuse, the cone case)
#                               or makes a new one per destination (random, the carrier-grade and VPN case)
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
	local ns=$1 wan=$2 lan=$3

	ip -n "$ns" link set lo up
	ip netns exec "$ns" sysctl -qw net.ipv4.ip_forward=1
	ip netns exec "$ns" iptables -t nat -A POSTROUTING -o "$wan" -j MASQUERADE $4
	ip netns exec "$ns" iptables -A FORWARD -i "$lan" -o "$wan" -j ACCEPT
	ip netns exec "$ns" iptables -A FORWARD -i "$wan" -o "$lan" -m conntrack --ctstate RELATED,ESTABLISHED -j ACCEPT
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

	# The registry's namespace is the internet: both routers are directly attached to it, which is what lets the
	# reachability check see a host arrive from a globally scoped address.
	link_pair "$PUBLIC_NS" pub-host "$HOST_WAN_PUBLIC" "$HOST_ROUTER_NS" wan-host "$HOST_ROUTER" 30
	link_pair "$PUBLIC_NS" pub-player "$PLAYER_WAN_PUBLIC" "$PLAYER_ROUTER_NS" wan-player "$PLAYER_ROUTER" 30

	link_pair "$HOST_ROUTER_NS" lan-host "${HOST_LAN}.1" "$HOST_NS" eth0 "${HOST_LAN}.2" 24
	link_pair "$PLAYER_ROUTER_NS" lan-player "${PLAYER_LAN}.1" "$PLAYER_NS" eth0 "${PLAYER_LAN}.2" 24

	local nat_argument=""
	if [[ -f $MODE_FILE && $(cat "$MODE_FILE") == random ]]; then nat_argument="--random"; fi

	configure_router "$HOST_ROUTER_NS" wan-host lan-host "$nat_argument"
	configure_router "$PLAYER_ROUTER_NS" wan-player lan-player "$nat_argument"

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
	if [[ $mode != reuse && $mode != random ]]; then
		echo "mapping takes 'reuse' or 'random'" >&2
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

case "${1:-}" in
	up) up ;;
	down) down ;;
	status) status ;;
	mapping) shift; mapping "${1:-}" ;;
	host) shift; run_in "$HOST_NS" "$@" ;;
	player) shift; run_in "$PLAYER_NS" "$@" ;;
	public) shift; run_in "$PUBLIC_NS" "$@" ;;
	*)
		echo "usage: $0 up|down|status|mapping reuse|random|host <cmd>|player <cmd>|public <cmd>" >&2
		exit 1
		;;
esac
