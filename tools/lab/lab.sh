#!/usr/bin/env bash
# MercyMode lab on Linux (the cloud container, or any Linux box with tModLoader).
#
#   tools/lab/lab.sh test [scenarios]   build, then play scripted battles headless and report (default: all)
#   tools/lab/lab.sh server             build, then run a normal dedicated server on the lab world (multiplayer later)
#   tools/lab/lab.sh reset              delete the lab world so the next run makes a fresh one
#
# Environment:
#   TML_DIR   tModLoader install (has tModLoader.dll)       default: $HOME/tml/install
#   LAB_DIR   lab saves (Mods, Worlds, results)             default: $HOME/mercylab
#   LAB_PORT  server port                                   default: 7778
#   LAB_SPEED game ticks per real tick in the test         default: 8
#
# The headless test turns a dedicated server into a stand-in single-player game (see Lab/LabSystem.cs). It needs
# no Terraria art or sound, so drawing isn't tested, but every battle rule is.
set -euo pipefail
here="$(cd "$(dirname "$0")/../.." && pwd)"
TML_DIR="${TML_DIR:-$HOME/tml/install}"
LAB_DIR="${LAB_DIR:-$HOME/mercylab}"
LAB_PORT="${LAB_PORT:-7778}"
mode="${1:-test}"
world="$LAB_DIR/Worlds/MercyLab.wld"

if [[ "$mode" == "reset" ]]; then
	rm -f "$LAB_DIR"/Worlds/MercyLab.*
	echo "Lab world removed."
	exit 0
fi

echo "== build"
(cd "$here" && dotnet build -c Release -nologo -v q 2>&1 | grep -E "error|warning|Build succeeded" | sort -u)
built="$(ls -t "$HOME/.local/share/Terraria/tModLoader/Mods/MercyMode.tmod" 2>/dev/null | head -1)"
[[ -f "$built" ]] || { echo "No MercyMode.tmod after the build"; exit 2; }

mkdir -p "$LAB_DIR/Mods" "$LAB_DIR/Worlds"
cp "$built" "$LAB_DIR/Mods/MercyMode.tmod"
echo '["MercyMode"]' > "$LAB_DIR/Mods/enabled.json"

args=(-server -nosteam -tmlsavedirectory "$LAB_DIR" -autocreate 1 -world "$world" -worldname MercyLab -port "$LAB_PORT" -players 8)
cd "$TML_DIR"
case "$mode" in
	test)
		scenarios="${2:-all}"
		rm -f "$LAB_DIR/lab-results.txt"
		echo "== lab: $scenarios"
		set +e
		MERCYMODE_LAB="$scenarios" MERCYMODE_LAB_OUT="$LAB_DIR" MERCYMODE_LAB_SPEED="${LAB_SPEED:-8}" timeout 1800 dotnet tModLoader.dll "${args[@]}" < /dev/null > "$LAB_DIR/server.out" 2>&1
		code=$?
		set -e
		grep "^\[LAB\]" "$LAB_DIR/server.out" | sed 's/^\[LAB\] //' || true
		if [[ $code -ne 0 && $code -ne 1 ]]; then
			echo "Server exited with $code; last lines:"
			tail -30 "$LAB_DIR/server.out"
		fi
		exit $code
		;;
	server)
		echo "== server on port $LAB_PORT (type 'exit' to stop)"
		exec dotnet tModLoader.dll "${args[@]}"
		;;
	*)
		echo "Unknown mode $mode (test | server | reset)"; exit 2
		;;
esac
