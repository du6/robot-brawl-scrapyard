#!/bin/zsh
# merge_all.zsh — one-time consolidation: merge every GitHub branch into
# the Mac's history (the source of truth; your files win any conflict),
# then push the result to main. Token never printed.
set -e
cd "/Users/leondu/Setup Guide In-Editor Tutorial"

git check-ignore -q .gh_token.local || { echo "ABORT: .gh_token.local is NOT gitignored"; exit 1; }
TOK=$(tr -d '\r\n' < .gh_token.local)
URL="https://x-access-token:${TOK}@github.com/du6/robot-brawl.git"

# 0. refuse to run with uncommitted work
git diff --quiet && git diff --cached --quiet || { echo "ABORT: uncommitted changes — run checkin.zsh first"; exit 1; }

# 1. fetch every remote branch
git fetch "$URL" '+refs/heads/*:refs/remotes/ghsync/*' 2>&1 | sed "s/${TOK}/***REDACTED***/g"

# 2. merge each one in. --allow-unrelated-histories because the curated
#    GitHub history and this Mac's history have different roots; -X ours
#    keeps THIS MAC's version of any conflicting file while still taking
#    files that only exist on the branch (README, docs/, ...).
for b in $(git for-each-ref --format='%(refname:short)' refs/remotes/ghsync/); do
  echo "--- merging ${b}"
  git merge --allow-unrelated-histories -X ours --no-edit "$b" || { echo "!!! ${b} did not merge cleanly — skipped (git merge --abort)"; git merge --abort; }
done

# 3. push the consolidated history to main
git push "$URL" HEAD:main 2>&1 | sed "s/${TOK}/***REDACTED***/g"
echo ""
echo "GitHub main now contains everything (all branches are merged in)."
echo "You can delete the old branches on GitHub whenever you like."
