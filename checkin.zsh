#!/bin/zsh
# checkin.zsh — commit the Unity project and push to a dated branch on
# github.com/du6/robot-brawl. Safe to re-run. Never prints the token.
# (Local history is UNRELATED to the curated repo history — always push
#  to a side branch, never to main. See Github_Push docs, 2026-08-02/04.)
set -e
cd "/Users/leondu/Setup Guide In-Editor Tutorial"

# 0. the token file must be gitignored, or we stop right here
git check-ignore -q .gh_token.local || { echo "ABORT: .gh_token.local is NOT gitignored"; exit 1; }

# 1. commit everything new/changed (gitignore keeps Library/token out)
git add -A
git commit -m "Check-in $(date +%Y-%m-%d_%H%M): v2 arc, V2.4 starter kit, V2.5 program library+picker, layout pass P1-P2" || echo "(nothing new to commit)"

# 2. push to a dated branch, token redacted from any output
TOK=$(tr -d '\r\n' < .gh_token.local)
BRANCH="owen-mac-$(date +%Y%m%d)"
git push "https://x-access-token:${TOK}@github.com/du6/robot-brawl.git" "HEAD:${BRANCH}" 2>&1 | sed "s/${TOK}/***REDACTED***/g"
echo ""
echo "Pushed to branch ${BRANCH} on du6/robot-brawl."
echo "Open a PR from it whenever you want it reviewed/merged."
