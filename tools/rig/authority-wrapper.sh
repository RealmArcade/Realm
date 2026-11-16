#!/usr/bin/env bash
# The client looks for this file beside the authority project when it launches one, and off Windows it looks for the name
# with no extension: a build that is a Godot project has no single executable to find, so this runs the project the way
# the game does on Windows, headless and with the arguments the client passed.
#
# It is installed by `tools/rig/realm-rig.sh wrapper on` and removed again by `wrapper off`, because it is a development
# aid rather than something a shipped build contains.
set -euo pipefail

GODOT_BIN=${GODOT_BIN:-/opt/realm-rig/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64}
PROJECT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)

# The client states some of the engine's own flags itself when it knows the authority is a Godot project on this
# platform, and an engine flag reaching the authority as a user argument is refused by it. They are dropped here and
# this launcher states the one it always needs.
user_args=()
for argument in "$@"; do
	case "$argument" in
		--headless | --) continue ;;
	esac
	user_args+=("$argument")
done

exec "$GODOT_BIN" --headless --path "$PROJECT_DIR" -- "${user_args[@]}"
