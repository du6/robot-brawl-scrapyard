#!/bin/zsh
# Package and deploy the headless worker, and schedule it.
#
#   zsh server/scripts/build_worker.zsh     # first: produce build/worker/
#   zsh server/scripts/deploy_worker.zsh
#
# Idempotent: re-running updates the job and the schedule in place.
#
# ⚠ SUPERSEDED AS THE PRIMARY WORKER, 2026-08-14. Owen raised the budget and
# the ladder now runs on an ALWAYS-ON Cloud Run worker pool — see
# deploy_worker_live.zsh, which reuses this script's image. This job and its
# scheduler tick remain deployed as the FALLBACK (the tick is paused; resume
# it to fall back). The cost reasoning below was true when written.
#
# ---------------------------------------------------------------------------
# WHY A CLOUD RUN *JOB* AND NOT A SERVICE, since that is the first question.
#
# A Cloud Run SERVICE must answer an HTTP health probe, and a poller serves
# nothing — the first attempt failed there, after four minutes of startup
# probes. It could have been forced with a dummy HTTP listener, and the real
# reason not to is money: a service warm enough to poll needs min-instances=1
# with CPU always allocated, about $35/mo of always-on CPU against a $25/mo
# budget whose database already takes $10-15. §7 says this scales to zero and
# costs ~$0 at prototype traffic.
#
# So the worker DRAINS AND EXITS (RB_MAX_IDLE) and Cloud Scheduler starts it.
# It bills only while there is work. The cost of that choice, stated plainly:
# a queued fight waits up to one scheduler period instead of starting at once.
# The ladder is asynchronous by design — you challenge and read the result
# later — so this trades latency nobody is waiting on for money that would
# otherwise be spent idling.
set -e

PROJECT_ID="${PROJECT_ID:-robot-brawl-ladder}"
REGION="${REGION:-us-central1}"
API_URL="${API_URL:-https://rb-api-902243335343.us-central1.run.app}"
SCHEDULE="${SCHEDULE:-*/5 * * * *}"
IMAGE="${REGION}-docker.pkg.dev/${PROJECT_ID}/rb/worker:$(date +%Y%m%d-%H%M%S)"
PROJ="$(cd "$(dirname "$0")/../.." && pwd)"
SA="$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')-compute@developer.gserviceaccount.com"

[ -x "$PROJ/build/worker/RobotBrawlWorker" ] || {
  echo "build/worker/RobotBrawlWorker is missing — run server/scripts/build_worker.zsh first."
  exit 2
}

cd "$PROJ"

# The build context is the REPO ROOT (the player lives in build/worker/), so
# the ignore file is what keeps this from uploading a 5 GB Library.
# ⚠ Its negations must re-open each PARENT directory before its children —
# gitignore semantics refuse to re-include a file whose parent is excluded, and
# the naive `*` + `!build/worker/**` uploaded a 54-byte context.
echo "building $IMAGE"
gcloud builds submit --project "$PROJECT_ID" --region "$REGION" \
  --service-account="projects/${PROJECT_ID}/serviceAccounts/${SA}" \
  --default-buckets-behavior=regional-user-owned-bucket \
  --ignore-file=.gcloudignore.worker \
  --config=/dev/stdin <<EOF
steps:
  - name: gcr.io/cloud-builders/docker
    args: ['build', '-f', 'server/Dockerfile.worker', '-t', '$IMAGE', '.']
images: ['$IMAGE']
options:
  machineType: E2_HIGHCPU_8
EOF

# RB_MAX_IDLE=3 with RB_IDLE_SECONDS=5 means "exit after ~15s of an empty
# queue". task-timeout caps a runaway drain; max-retries=1 because a worker
# that died mid-job has already left that job to the visibility timeout, and
# retrying the whole container would just claim more.
COMMON=(--image "$IMAGE" --project "$PROJECT_ID" --region "$REGION"
        --cpu=2 --memory=2Gi --task-timeout=30m --max-retries=1
        --set-env-vars="RB_API_URL=${API_URL},RB_WORKER_KINDS=BOTH,RB_IDLE_SECONDS=5,RB_MAX_IDLE=3,RB_WORKER_ID=cloud-worker"
        --set-secrets=RB_WORKER_KEY=rb-worker-key:latest)

if gcloud run jobs describe rb-worker --project "$PROJECT_ID" --region "$REGION" >/dev/null 2>&1; then
  echo "updating the job"
  gcloud run jobs update rb-worker "${COMMON[@]}" --quiet
else
  echo "creating the job"
  gcloud run jobs create rb-worker "${COMMON[@]}" --quiet
fi

# ⚠ WITHOUT THIS THE SCHEDULE SILENTLY DOES NOTHING. Cloud Scheduler
# authenticates as this service account, and a job with no IAM policy refuses
# it — the scheduler reports no error anywhere obvious and simply never
# produces an execution. Measured: the first three forced runs did nothing at
# all until this binding was added.
echo "granting run.invoker so the scheduler can actually start it"
gcloud run jobs add-iam-policy-binding rb-worker \
  --project "$PROJECT_ID" --region "$REGION" \
  --member="serviceAccount:${SA}" --role="roles/run.invoker" >/dev/null

gcloud services enable cloudscheduler.googleapis.com --project "$PROJECT_ID" >/dev/null 2>&1 || true

URI="https://${REGION}-run.googleapis.com/apis/run.googleapis.com/v1/namespaces/${PROJECT_ID}/jobs/rb-worker:run"
if gcloud scheduler jobs describe rb-worker-tick --project "$PROJECT_ID" --location "$REGION" >/dev/null 2>&1; then
  echo "updating the schedule"
  gcloud scheduler jobs update http rb-worker-tick \
    --project "$PROJECT_ID" --location "$REGION" \
    --schedule="$SCHEDULE" --time-zone=UTC --uri="$URI" --http-method=POST \
    --oauth-service-account-email="$SA" >/dev/null
else
  echo "creating the schedule"
  gcloud scheduler jobs create http rb-worker-tick \
    --project "$PROJECT_ID" --location "$REGION" \
    --schedule="$SCHEDULE" --time-zone=UTC --uri="$URI" --http-method=POST \
    --oauth-service-account-email="$SA" \
    --description="Drain the ladder queue. The worker exits when the queue is empty, so this costs nothing while idle." >/dev/null
fi

echo ""
echo "deployed $IMAGE"
echo "schedule: $SCHEDULE (UTC)"
echo ""
echo "run it now:    gcloud run jobs execute rb-worker --project $PROJECT_ID --region $REGION"
echo "watch it:      gcloud logging read 'resource.type=\"cloud_run_job\" AND textPayload:\"WorkerHost\"' --project $PROJECT_ID --limit 10"
