#!/bin/zsh
# Build the headless Linux worker from a CLONE of the project.
#
#   zsh server/scripts/build_worker.zsh [output-dir]
#
# ⚠ WHY A CLONE, AND WHY THIS IS NOT PARANOIA.
#
# Unity holds a per-project lock, so a batchmode build cannot run against the
# project owen has open. Worse, building SWITCHES THE ACTIVE BUILD TARGET, and
# this project's target is iOS — that is where the TestFlight soft launch
# lives (§M4). Switching to Linux and back reimports every asset twice and
# leaves the editor on the wrong platform if anything fails in between.
#
# A clone costs ~550 MB of source plus a Library that Unity regenerates on
# first import (several GB, and the slow part — expect 10-20 minutes the first
# time, then it is cached). Disk was 24 GB free when this was written; if that
# is tight, delete $CLONE afterwards.
#
# The clone is source only: Assets, Packages, ProjectSettings. Copying Library
# would carry iOS-target artefacts into a Linux build, which is the kind of
# thing that half-works.
set -e

PROJ="${PROJ:-$(cd "$(dirname "$0")/../.." && pwd)}"
CLONE="${CLONE:-/tmp/rb-worker-build}"
OUT="${1:-$PROJ/build/worker}"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity}"

[ -x "$UNITY" ] || { echo "Unity not found at $UNITY — set UNITY=..."; exit 2; }

echo "project : $PROJ"
echo "clone   : $CLONE"
echo "output  : $OUT"

mkdir -p "$CLONE"
# -a preserves the .meta files, which ARE the project: a copy that loses them
# reimports as a different project with every reference broken.
echo "syncing source into the clone…"
for d in Assets Packages ProjectSettings; do
  rsync -a --delete "$PROJ/$d/" "$CLONE/$d/"
done

echo "building (first run also imports the project — this is the slow part)…"
set +e
"$UNITY" -quit -batchmode -nographics \
  -projectPath "$CLONE" \
  -buildTarget Linux64 \
  -executeMethod RobotBrawl.Editor.BuildWorker.Build \
  -rbOutDir "$OUT" \
  -logFile - 2>&1 | tail -40
STATUS=${pipestatus[1]}
set -e

if [ "$STATUS" -ne 0 ]; then
  echo ""
  echo "BUILD FAILED (exit $STATUS). The full log is above; BuildWorker exits"
  echo "non-zero on purpose so this cannot be packaged and deployed anyway."
  exit "$STATUS"
fi

echo ""
if [ -x "$OUT/RobotBrawlWorker" ]; then
  echo "built: $OUT/RobotBrawlWorker"
  du -sh "$OUT" 2>/dev/null
else
  echo "build reported success but $OUT/RobotBrawlWorker is missing"
  exit 1
fi
