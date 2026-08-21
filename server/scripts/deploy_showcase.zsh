#!/bin/zsh
# deploy_showcase.zsh — build and schedule the champions-page refresher.
#
# cyberduck.club/champions/ is a STATIC BAKE of the ladder's top robot per
# weight class. It goes stale the moment anyone plays: it was four-fifths
# wrong two days after baking, then wrong again three hours later. This puts
# the refresh on Cloud Scheduler so it does not depend on owen's laptop being
# awake and logged in.
#
# Idempotent: safe to re-run. Creates the job+schedule the first time and
# updates them after.
#
# Cost: one tiny container run per day, a few seconds each. Effectively free
# against the $25/mo budget — unlike an always-on service.
set -e

PROJECT_ID="${PROJECT_ID:-robot-brawl-ladder}"
REGION="${REGION:-us-central1}"
API_URL="${API_URL:-https://rb-api-902243335343.us-central1.run.app}"
# 09:00 America/Los_Angeles, matching the laptop job it replaces. Named zone
# rather than a UTC offset so it does not drift an hour at DST.
SCHEDULE="${SCHEDULE:-0 9 * * *}"
TZ_NAME="${TZ_NAME:-America/Los_Angeles}"
IMAGE="${REGION}-docker.pkg.dev/${PROJECT_ID}/rb/showcase:$(date +%Y%m%d-%H%M%S)"
PROJ="$(cd "$(dirname "$0")/../.." && pwd)"
SA="$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')-compute@developer.gserviceaccount.com"

cd "$PROJ"

echo "building $IMAGE"
gcloud builds submit --project "$PROJECT_ID" --region "$REGION" \
  --service-account="projects/${PROJECT_ID}/serviceAccounts/${SA}" \
  --default-buckets-behavior=regional-user-owned-bucket \
  --ignore-file=.gcloudignore.showcase \
  --config=/dev/stdin <<EOF
steps:
  - name: gcr.io/cloud-builders/docker
    args: ['build', '-f', 'website/tools/Dockerfile.showcase', '-t', '$IMAGE', '.']
images: ['$IMAGE']
EOF

# The token is a SECRET, never an env var in this file and never in the image.
# It needs only `contents:write` on du6/cyberduck-club.
COMMON=(--image "$IMAGE" --project "$PROJECT_ID" --region "$REGION"
        --cpu=1 --memory=512Mi --task-timeout=10m --max-retries=1
        --set-env-vars="RB_API_URL=${API_URL},SITE_OWNER=du6,SITE_REPO=cyberduck-club"
        --set-secrets=GH_TOKEN=cyberduck-site-token:latest)

if gcloud run jobs describe rb-showcase --project "$PROJECT_ID" --region "$REGION" >/dev/null 2>&1; then
  echo "updating the job"
  gcloud run jobs update rb-showcase "${COMMON[@]}" --quiet
else
  echo "creating the job"
  gcloud run jobs create rb-showcase "${COMMON[@]}" --quiet
fi

echo "granting the job's identity access to the token"
gcloud secrets add-iam-policy-binding cyberduck-site-token \
  --project "$PROJECT_ID" \
  --member="serviceAccount:${SA}" --role="roles/secretmanager.secretAccessor" >/dev/null

# ⚠ WITHOUT THIS THE SCHEDULE SILENTLY DOES NOTHING — the same trap documented
# in deploy_worker.zsh. Cloud Scheduler authenticates as this service account,
# and a job with no IAM policy refuses it with no error anywhere obvious: the
# schedule simply never produces an execution.
echo "granting run.invoker so the scheduler can actually start it"
gcloud run jobs add-iam-policy-binding rb-showcase \
  --project "$PROJECT_ID" --region "$REGION" \
  --member="serviceAccount:${SA}" --role="roles/run.invoker" >/dev/null

gcloud services enable cloudscheduler.googleapis.com --project "$PROJECT_ID" >/dev/null 2>&1 || true

URI="https://${REGION}-run.googleapis.com/apis/run.googleapis.com/v1/namespaces/${PROJECT_ID}/jobs/rb-showcase:run"
if gcloud scheduler jobs describe rb-showcase-daily --project "$PROJECT_ID" --location "$REGION" >/dev/null 2>&1; then
  echo "updating the schedule"
  gcloud scheduler jobs update http rb-showcase-daily \
    --project "$PROJECT_ID" --location "$REGION" \
    --schedule="$SCHEDULE" --time-zone="$TZ_NAME" --uri="$URI" --http-method=POST \
    --oauth-service-account-email="$SA" >/dev/null
else
  echo "creating the schedule"
  gcloud scheduler jobs create http rb-showcase-daily \
    --project "$PROJECT_ID" --location "$REGION" \
    --schedule="$SCHEDULE" --time-zone="$TZ_NAME" --uri="$URI" --http-method=POST \
    --oauth-service-account-email="$SA" \
    --description="Re-bake cyberduck.club/champions/ from the live ladder. Fails closed: a bad bake is never published." >/dev/null
fi

echo ""
echo "deployed $IMAGE"
echo "schedule: $SCHEDULE ($TZ_NAME)"
echo "run it now:  gcloud run jobs execute rb-showcase --project $PROJECT_ID --region $REGION"
echo "logs:        gcloud run jobs executions list --job rb-showcase --project $PROJECT_ID --region $REGION"
