#!/bin/zsh
# Schedule the season rollover — the caller the endpoint never had.
#
#   zsh server/scripts/deploy_season_scheduler.zsh
#
# Idempotent: re-running updates the job in place.
#
# ---------------------------------------------------------------------------
# WHY DAILY, WHEN A SEASON IS FOUR WEEKS. The endpoint owns the calendar, not
# the cron: an UNFORCED POST /v1/admin/season/rollover answers 200
# rolled=false until the current season's ends_at has passed, and the very
# first tick after deploy STARTS season 1's clock rather than closing it
# (there is no seasons row until something writes one). So a daily tick
# rolls the season on the exact day it expires, follows season_weeks if owen
# retunes it in ladder_config, and costs one HTTP request a day. A "every 4
# weeks" cron would drift from the config and silently disagree with it.
#
# The refusal is 200 ON PURPOSE — Cloud Scheduler retries and alerts on
# non-2xx, and "not due yet" is this job's normal outcome ~27 days in 28.
#
# AUTH: the endpoint checks X-Worker-Key, same trust model as the fight
# worker. The key is read out of Secret Manager AT DEPLOY TIME and stored in
# the scheduler job's config — the same place the worker job already keeps
# it, so this widens nothing.
set -e

PROJECT_ID="${PROJECT_ID:-robot-brawl-ladder}"
REGION="${REGION:-us-central1}"
API_URL="${API_URL:-https://rb-api-902243335343.us-central1.run.app}"
# 09:30 UTC: after the 09:00 UTC DB backup, so the last pre-rollover state is
# always restorable — a rollover pays scrap, and §5's restore drill is the
# net under every faucet.
SCHEDULE="${SCHEDULE:-30 9 * * *}"
URI="$API_URL/v1/admin/season/rollover"

WKEY="$(gcloud secrets versions access latest --secret=rb-worker-key --project "$PROJECT_ID")"
[ -n "$WKEY" ] || { echo "could not read rb-worker-key from Secret Manager"; exit 2; }

gcloud services enable cloudscheduler.googleapis.com --project "$PROJECT_ID" >/dev/null 2>&1 || true

if gcloud scheduler jobs describe rb-season-rollover --project "$PROJECT_ID" --location "$REGION" >/dev/null 2>&1; then
  echo "updating rb-season-rollover"
  gcloud scheduler jobs update http rb-season-rollover \
    --project "$PROJECT_ID" --location "$REGION" \
    --schedule="$SCHEDULE" --time-zone=UTC --uri="$URI" --http-method=POST \
    --update-headers="X-Worker-Key=$WKEY" >/dev/null
else
  echo "creating rb-season-rollover"
  gcloud scheduler jobs create http rb-season-rollover \
    --project "$PROJECT_ID" --location "$REGION" \
    --schedule="$SCHEDULE" --time-zone=UTC --uri="$URI" --http-method=POST \
    --headers="X-Worker-Key=$WKEY" \
    --description="Daily season-rollover tick. The endpoint owns the calendar: it answers rolled=false until the season's ends_at passes, so this fires ~27 no-ops per roll. See season_weeks in ladder_config." >/dev/null
fi

echo "scheduled: POST $URI daily at $SCHEDULE UTC"
echo "first tick STARTS season 1's clock; the roll happens season_weeks later."