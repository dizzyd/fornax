#!/usr/bin/env bash
#
# Run the fornax suite against every mod set and config permutation that matters.
#
#   bash scripts/test-matrix.sh [user@host]
#
# A green run of the suite on its own proves less than it looks. Three defects in this mod were
# invisible with fornax loaded alone and obvious the moment a real mod was in the world, and two
# more only showed up under a non-default config. So: four mod sets x the whole suite, then the
# startup path at six settings.
#
# Everything is provisioned with cairn, so a fresh box needs nothing but a checkout of vstestkit
# and cairn-cli on PATH or in ~/bin. The tests in tests/CompatTests.cs skip themselves when the
# mod they are about is absent, so mod set A is just the ordinary suite.
#
# Takes about ten minutes. The box is shared: everything here runs in this mod's own slot.

set -euo pipefail

HOST="${1:-dizzyd@vsclient.home}"
GAME="${VSTK_GAME_VERSION:-1.22.6}"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$REPO/fornax"                       # the csproj tree; tests/ sits at the repo root
TESTKIT="$(cd "$REPO/.." && pwd)/vstestkit"

[ -d "$TESTKIT" ] || { echo "no vstestkit at $TESTKIT" >&2; exit 1; }
[ -d "$PROJECT" ] || { echo "no mod project at $PROJECT" >&2; exit 1; }

echo "== building and syncing to $HOST"
bash "$TESTKIT/scripts/sync-linux.sh" "$HOST" --mod "$PROJECT" >/dev/null
# sync-linux.sh mirrors the tests directory, so the compat suite goes over with everything else.

echo "== provisioning mod sets with cairn (idempotent)"
ssh "$HOST" GAME="$GAME" 'bash -s' <<'PROVISION'
set -eu
CAIRN="$(command -v cairn-cli || echo "$HOME/bin/cairn-cli")"
[ -x "$CAIRN" ] || { echo "cairn-cli not found on the test box" >&2; exit 1; }

pack() {
    id="$1"; shift
    if [ ! -f "$HOME/.cairn/packs/$id/pack.json" ]; then
        "$CAIRN" init "$id" --game "$GAME" >/dev/null
    fi
    for modid in "$@"; do
        "$CAIRN" add "$id" "$modid" >/dev/null 2>&1 || true
    done
    "$CAIRN" sync "$id" 2>&1 | sed 's/^/    /'
}

# The mods from the incompatibility report, plus configlib for the settings GUI and server sync.
# Dependencies come along on their own: bricklayers pulls em, configlib pulls vsimgui.
pack fornaxcompat bulkquicklime kilnshelves ceramicbucketbarrel bricklayers
pack cfgprobe     configlib
PROVISION

echo
echo "=================== MOD SETS (whole suite, client tier)"
ssh "$HOST" 'bash -s' <<'MODSETS'
set -eu
cd ~/vstestkit-fornax
CFG=~/.cairn/packs/cfgprobe/Mods
COMPAT=~/.cairn/packs/fornaxcompat/Mods

bash scripts/stop.sh >/dev/null 2>&1 || true

set_run() {
    label="$1"; shift
    echo "---- $label"
    bash scripts/run.sh ~/mods/fornax/tests --mod ~/mods/fornax/fornax --client "$@" 2>&1 \
        | grep -E "^(FAIL|err)|passed" | sed 's/^/     /'
}

set_run "A  fornax alone"
set_run "B  + configlib + vsimgui"  --mods "$CFG"
set_run "C  + the report's mods"    --mods "$COMPAT"
set_run "D  everything"             --mods "$CFG:$COMPAT"
MODSETS

echo
echo "=================== CONFIG PERMUTATIONS (startup path)"
ssh "$HOST" 'bash -s' <<'PERMS'
set -eu
cd ~/vstestkit-fornax
CONF=~/vstestkit-fornax/run/fornax/data/ModConfig/fornax.json

# boot.sh rm -rf's the run directory unless VSTK_KEEP is set, which would delete the very config
# file these are varying before StartPre ever reads it. Without this every permutation silently
# tests the defaults and passes, which is worse than failing.
export VSTK_KEEP=1
bash scripts/stop.sh >/dev/null 2>&1 || true

# a first boot to generate the config, so there is something to edit
bash scripts/run.sh ~/mods/fornax/tests --mod ~/mods/fornax/fornax --filter BehaviourMatchesWhatever >/dev/null 2>&1 || true
bash scripts/stop.sh >/dev/null 2>&1 || true

perm() {
    label="$1"; lime="$2"; beehive="$3"; containers="$4"; maxfireable="$5"
    python3 - "$CONF" "$lime" "$beehive" "$containers" "$maxfireable" <<'PY'
import json, sys
path = sys.argv[1]
cfg = json.load(open(path))
cfg["FireLime"] = sys.argv[2] == "T"
cfg["FireLimeInBeehiveKiln"] = sys.argv[3] == "T"
cfg["FireContainersInChamber"] = sys.argv[4] == "T"
cfg["RespectMaxFireable"] = sys.argv[5] == "T"
json.dump(cfg, open(path, "w"), indent=2)
PY
    echo "---- $label  (lime=$lime beehive=$beehive containers=$containers maxFireable=$maxfireable)"
    bash scripts/run.sh ~/mods/fornax/tests --mod ~/mods/fornax/fornax \
         --filter BehaviourMatchesWhatever 2>&1 \
        | grep -E "^(ok|FAIL|err)|passed|config   :|lime tags:|wares=" | sed 's/^/     /'
    bash scripts/stop.sh >/dev/null 2>&1 || true
}

#     label         lime beehive containers maxFireable
perm "defaults"      T    F       F          F
perm "all off"       F    F       F          F
perm "all on"        T    T       T          T
perm "lime split"    F    T       T          F
perm "cap only"      T    F       F          T
perm "restore"       T    F       F          F
PERMS

echo
echo "done."
