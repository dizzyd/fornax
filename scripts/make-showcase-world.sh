#!/usr/bin/env bash
#
# Builds a savegame containing one updraft kiln per construction stage, and installs it
# where <mod>/scripts/runClient.sh expects to find it.
#
#   bash scripts/make-showcase-world.sh
#   bash scripts/make-showcase-world.sh --keep     # leave the build session running
#
# vstestkit is used only as a way to run code against a live world; it registers no
# blocks or items of its own, so the savegame it produces opens with just this mod.
set -euo pipefail

MOD_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO_ROOT="$(cd "$MOD_ROOT/.." && pwd)"
TESTKIT="${VSTK_ROOT:-$REPO_ROOT/vstestkit}"
[ -d "$TESTKIT" ] || { echo "vstestkit not found at $TESTKIT; set VSTK_ROOT" >&2; exit 1; }

: "${VINTAGE_STORY:=$(ls -d "$HOME"/.cairn/games/*.app 2>/dev/null | sort -V | tail -1)}"
: "${VS_DEV_DATA:=$HOME/.vintagestorydev}"
export VINTAGE_STORY VS_DEV_DATA

WORLD_NAME="${WORLD_NAME:-fornaxcreative}"
BUILD_RUN="${BUILD_RUN:-$MOD_ROOT/.showcase-run}"

echo "==> building the mod"
dotnet build "$MOD_ROOT/fornax/fornax.csproj" -c Debug -v quiet --nologo >/dev/null

echo "==> booting a headless world"
# Stop before wiping: stop.sh reads the pid file out of the run dir, and the game
# binds a fixed port, so a survivor makes the next boot die on "address in use".
( cd "$TESTKIT" && VSTK_RUN="$BUILD_RUN" bash scripts/stop.sh >/dev/null 2>&1 || true )
( cd "$TESTKIT" && bash scripts/stop.sh >/dev/null 2>&1 || true )
rm -rf "$BUILD_RUN"
( cd "$TESTKIT" && VSTK_RUN="$BUILD_RUN" VSTK_TIME=1 \
    VSTK_EXTRA_MODS="$MOD_ROOT/fornax/bin/Debug/Mods" \
    VSTK_EXTRA_ORIGINS="$MOD_ROOT/fornax/assets" \
    bash scripts/boot.sh )

echo "==> loading chunks"
ev() { ( cd "$TESTKIT" && VSTK_RUN="$BUILD_RUN" bash scripts/vstk eval -f "$MOD_ROOT/scripts/$1.cs" 2>/dev/null ); }

# The endpoint answers before the world is fully up: for a few seconds after boot
# the default spawn is still unset, and reading it throws.
for _ in $(seq 1 30); do
    OUT="$(ev showcase-preload || true)"
    case "$OUT" in *'"ok": true'*) break ;; esac
    sleep 1
done
case "$OUT" in *'"ok": true'*) ;; *) echo "$OUT" >&2; echo "world never became ready" >&2; exit 1 ;; esac

for _ in $(seq 1 40); do
    READY="$(ev showcase-ready | sed -n 's/.*"value": "\([0-9]*\/[0-9]*\)".*/\1/p')"
    [ -n "$READY" ] && echo "    chunks $READY"
    [ -n "$READY" ] && [ "${READY%%/*}" = "${READY##*/}" ] && break
    sleep 1
done

echo "==> placing the kilns"
ev showcase-world | sed -n 's/.*"value": "\(.*\)".*/    \1/p'

echo "==> saving"
# scripts/stop.sh shuts the endpoint down without writing the world - test worlds are
# meant to be disposable. SIGTERM is the path that actually runs the game's own save.
PID="$(cat "$BUILD_RUN/server.pid")"
kill -TERM "$PID"
for _ in $(seq 1 60); do kill -0 "$PID" 2>/dev/null || break; sleep 1; done
rm -f "$BUILD_RUN/server.pid"
grep -m1 "World saved!" "$BUILD_RUN/data/Logs/server-main.log" | sed 's/^/    /' || {
    echo "the server exited without saving" >&2; exit 1; }

echo "==> verifying the savegame"
# boot.sh wipes its run directory unless told not to; without this the check would
# silently examine a freshly generated world and always find nothing.
( cd "$TESTKIT" && VSTK_RUN="$BUILD_RUN" VSTK_KEEP=1 VSTK_TIME=1 \
    VSTK_EXTRA_MODS="$MOD_ROOT/fornax/bin/Debug/Mods" \
    VSTK_EXTRA_ORIGINS="$MOD_ROOT/fornax/assets" \
    bash scripts/boot.sh >/dev/null 2>&1 )
for _ in $(seq 1 30); do
    OUT="$(ev showcase-preload || true)"
    case "$OUT" in *'"ok": true'*) break ;; esac
    sleep 1
done
for _ in $(seq 1 40); do
    READY="$(ev showcase-ready | sed -n 's/.*"value": "\([0-9]*\/[0-9]*\)".*/\1/p')"
    [ -n "$READY" ] && [ "${READY%%/*}" = "${READY##*/}" ] && break
    sleep 1
done
FOUND="$(ev showcase-verify | sed -n 's/.*"value": "\(.*\)".*/\1/p')"
echo "    $FOUND"
( cd "$TESTKIT" && VSTK_RUN="$BUILD_RUN" bash scripts/stop.sh >/dev/null 2>&1 || true )
case "$FOUND" in
    *kilnfirebox*) ;;
    *) echo "the saved world contains no kilns" >&2; exit 1 ;;
esac

SRC="$BUILD_RUN/data/Saves/vstestkit.vcdbs"
[ -f "$SRC" ] || { echo "no savegame produced at $SRC" >&2; exit 1; }
if [ -f "$SRC-wal" ]; then
    echo "    checkpointing write-ahead log into the savegame"
    sqlite3 "$SRC" "PRAGMA wal_checkpoint(TRUNCATE);" >/dev/null
fi

# A .vcdbs is SQLite in WAL mode. Copying just the main file over an older world
# leaves that world's -wal and -shm behind, and SQLite then replays pages belonging
# to a DIFFERENT database image on top of the new one - which the game reports as a
# corrupted save. The sidecars must go with it.
install_world() {
    local dest="$1"
    mkdir -p "$(dirname "$dest")"
    rm -f "$dest" "$dest-wal" "$dest-shm" "$dest-journal"
    cp "$SRC" "$dest"
    local check
    check="$(sqlite3 "$dest" 'PRAGMA integrity_check;' 2>&1 | head -1)"
    [ "$check" = "ok" ] || { echo "installed world failed integrity check: $check" >&2; exit 1; }

    # Opening a WAL database recreates -wal/-shm, so the check that guards against
    # stale sidecars leaves a fresh pair behind. Fold anything pending back into the
    # main file and clear them, so what ships is one self-contained file.
    sqlite3 "$dest" "PRAGMA wal_checkpoint(TRUNCATE);" >/dev/null 2>&1 || true
    rm -f "$dest-wal" "$dest-shm"
}

DEST="$VS_DEV_DATA/Saves/$WORLD_NAME.vcdbs"
install_world "$DEST"

echo
echo "installed: $DEST  ($(du -h "$DEST" | cut -f1))"

# --- also make it launchable through Cairn -----------------------------------
# A Cairn pack has its own Mods/ and data/, and `launch` runs the game with
# --dataPath <pack>/data --addModPath <pack>/Mods. Syncing only touches mods the
# pack.json lists, so a hand-dropped zip survives it.
#
# Note this installs the PACKAGED zip, not the build tree: runClient.sh gets the
# mod's assets through --addOrigin, and Cairn has no equivalent, so a code-only
# build tree would register no blocks at all.
CAIRN="${CAIRN_CLI:-$HOME/src/cairn/artifacts/osx-arm64/cairn-cli}"
if [ -x "$CAIRN" ] && [ "${NO_CAIRN:-0}" != "1" ]; then
    ZIP="$(ls -t "$MOD_ROOT"/Releases/fornax_*.zip 2>/dev/null | head -1)"
    if [ -z "$ZIP" ]; then
        echo "==> no release zip; building one"
        ( cd "$MOD_ROOT" && ./build.sh >/dev/null )
        ZIP="$(ls -t "$MOD_ROOT"/Releases/fornax_*.zip 2>/dev/null | head -1)"
    fi

    PACK_ID="${PACK_ID:-fornax}"
    GAME_VERSION="$(basename "$VINTAGE_STORY" .app)"
    PACK_DIR="$HOME/.cairn/packs/$PACK_ID"

    [ -d "$PACK_DIR" ] || "$CAIRN" init "$PACK_ID" --game "$GAME_VERSION" >/dev/null
    mkdir -p "$PACK_DIR/Mods" "$PACK_DIR/data/Saves"
    rm -f "$PACK_DIR"/Mods/fornax_*.zip
    cp "$ZIP" "$PACK_DIR/Mods/"
    install_world "$PACK_DIR/data/Saves/Updraft Kiln Stages.vcdbs"

    echo "cairn pack: $PACK_ID  ($(basename "$ZIP") + the showcase world)"
    echo
    echo "launch it either way:"
    echo "  $CAIRN launch $PACK_ID                     # the packaged mod"
    echo "  cd $MOD_ROOT/fornax && bash scripts/runClient.sh \"\$VINTAGE_STORY\" \"\$PWD\" Debug"
else
    echo
    echo "open it with:"
    echo "  cd $MOD_ROOT/fornax && bash scripts/runClient.sh \"\$VINTAGE_STORY\" \"\$PWD\" Debug"
fi
