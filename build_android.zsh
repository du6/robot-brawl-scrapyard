#!/bin/zsh
# Build the Android .aab from a CLONE of the project.
#
#   zsh build_android.zsh [output-dir]          # release .aab (Play artifact)
#   RB_APK=1 zsh build_android.zsh [output-dir] # sideloadable .apk instead
#
# ⚠ WHY A CLONE: same reason as server/scripts/build_worker.zsh, quoted here
# because it bites — Unity holds a per-project lock (owen's editor is open on
# the real project), and building SWITCHES THE ACTIVE BUILD TARGET. This
# project's target is iOS; that is where the App Store submission lives.
# The clone is source only (Assets, Packages, ProjectSettings) — Library
# regenerates on first import, which is the slow part: expect 20-40 minutes
# the first time (full Android import + IL2CPP), minutes after.
set -e

PROJ="${PROJ:-$(cd "$(dirname "$0")" && pwd)}"
CLONE="${CLONE:-/tmp/rb-android-build}"
OUT="${1:-$PROJ/build/android}"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.5.4f1/Unity.app/Contents/MacOS/Unity}"
METHOD="RobotBrawl.Editor.BuildAndroid.Build"
[ -n "$RB_APK" ] && METHOD="RobotBrawl.Editor.BuildAndroid.BuildApk"

[ -x "$UNITY" ] || { echo "Unity not found at $UNITY — set UNITY=..."; exit 2; }

echo "project : $PROJ"
echo "clone   : $CLONE"
echo "output  : $OUT"
echo "method  : $METHOD"

mkdir -p "$CLONE"
# -a preserves the .meta files, which ARE the project (build_worker.zsh's note).
echo "syncing source into the clone…"
for d in Assets Packages ProjectSettings; do
  rsync -a --delete "$PROJ/$d/" "$CLONE/$d/"
done

echo "building (first run also imports the project — this is the slow part)…"
set +e
"$UNITY" -quit -batchmode -nographics \
  -projectPath "$CLONE" \
  -buildTarget Android \
  -executeMethod "$METHOD" \
  -rbOutDir "$OUT" \
  -logFile - 2>&1 | tail -40
STATUS=${pipestatus[1]}
set -e

if [ "$STATUS" -ne 0 ]; then
  echo ""
  echo "BUILD FAILED (exit $STATUS). BuildAndroid exits non-zero on purpose"
  echo "so a broken artifact cannot be uploaded anyway."
  exit "$STATUS"
fi

echo ""
ls -la "$OUT"
echo "DONE. Debug-signed until the upload keystore exists — see BuildAndroid.cs header."
