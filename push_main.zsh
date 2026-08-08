#!/bin/zsh
# push_main.zsh - commit today's repair work and fast-forward GitHub main.
#
# Context (2026-08-08): merge_all.zsh put this Mac's history onto main this
# morning, and that merge did not compile. This pushes the REPAIRED tree on
# top of the same lineage, so main compiles again. Never prints the token.
#
# NOTE: there is no 'origin' remote in .git/config - both of your scripts
# push by full URL with an inline token, so 'git push origin main' cannot
# work. This mirrors checkin.zsh, but targets main instead of a dated branch.
set -e
cd "/Users/leondu/Setup Guide In-Editor Tutorial"

git check-ignore -q .gh_token.local || { echo "ABORT: .gh_token.local is NOT gitignored"; exit 1; }

git add -A
git commit -m "Repair the merge_all consolidation; fix SHOP description row height on first open

- BuilderManager: drop the 48-line duplicate ModeBanner block (CS0102/CS0111)
- MobileBuilderUI: drop the stale duplicate of the material-chooser block, and
  the half-applied swatch call sites the merge brought in without declarations
- MobileBuilderUI: size the SHOP description row at the header row's width, so
  the first open measures its wrap at 1044 units instead of the default 74
- CareerSmoke: assert every description row is sized for its text on FIRST open
- TouchSmoke: career fixture tops stock up to one free, whatever the build holds

CareerSmoke 128/128, TouchSmoke 27/27, MatrixBench 9/9 (two runs)." || echo "(nothing new to commit)"

TOK=$(tr -d '\r\n' < .gh_token.local)
git push "https://x-access-token:${TOK}@github.com/du6/robot-brawl.git" HEAD:main 2>&1 | sed "s/${TOK}/***REDACTED***/g"
echo ""
echo "Pushed $(git rev-parse --short HEAD) to main on du6/robot-brawl."
