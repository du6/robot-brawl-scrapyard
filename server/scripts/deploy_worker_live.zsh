#!/bin/zsh
# Deploy the ALWAYS-ON worker: a Cloud Run WORKER POOL running the same
# image as the rb-worker job, with RB_MAX_IDLE=0 so the host polls forever.
#
#   zsh server/scripts/deploy_worker_live.zsh              # reuse job's image
#   IMAGE=...:tag zsh server/scripts/deploy_worker_live.zsh
#
# HISTORY, because the old note argued the opposite. deploy_worker.zsh chose
# a JOB + 5-minute Cloud Scheduler tick to fit a $25/mo budget, and priced
# the always-on alternative at ~$35/mo it did not have. On 2026-08-14 owen
# raised the budget and asked for the always-on worker by name — and Cloud
# Run WORKER POOLS (no HTTP probe, no dummy listener; the thing that made
# the first service attempt fail is simply not required) had shipped in the
# meantime. Measured on deploy day: enlist -> validated on the live ladder
# in ~5 seconds, the whole 10-job seed drained in under 3 seconds of work.
#
# The loop was in the player all along: RB_MAX_IDLE=0 means "never exit"
# (WorkerHost's documented default) — always-on is a deployment shape, not
# a code change.
#
# Cost: owen took the dial down to 1 vCPU + 1 GiB on deploy day
# (~$50-55/mo list) after 2/2 was quoted at ~$100-110/mo. Validate latency
# re-measured identical at the smaller size; fight wall-time roughly
# doubles vs 2 vCPU, which an async ladder does not feel.
#
# The old job + scheduler stay deployed as the fallback: rb-worker-tick is
# PAUSED, not deleted. Concurrent workers are safe (the claim contract is
# atomic), so resuming the tick alongside the pool merely wastes money.
set -e

PROJECT_ID="${PROJECT_ID:-robot-brawl-ladder}"
REGION="${REGION:-us-central1}"
API_URL="${API_URL:-https://rb-api-902243335343.us-central1.run.app}"

# Default to whatever image the job currently runs — one artifact, two shapes.
IMAGE="${IMAGE:-$(gcloud run jobs describe rb-worker --project "$PROJECT_ID" --region "$REGION" \
  --format='value(spec.template.spec.template.spec.containers[0].image)')}"
[ -n "$IMAGE" ] || { echo "no IMAGE and could not read the job's image"; exit 2 }

echo "deploying worker pool rb-worker-live from $IMAGE"
gcloud run worker-pools deploy rb-worker-live \
  --project "$PROJECT_ID" --region "$REGION" \
  --image "$IMAGE" \
  --cpu=1 --memory=1Gi --instances=1 \
  --set-env-vars="RB_API_URL=${API_URL},RB_WORKER_KINDS=BOTH,RB_IDLE_SECONDS=5,RB_MAX_IDLE=0,RB_WORKER_ID=cloud-worker-live" \
  --set-secrets=RB_WORKER_KEY=rb-worker-key:latest

echo "pausing the 5-minute tick (the pool replaces it; resume to fall back)"
gcloud scheduler jobs pause rb-worker-tick \
  --project "$PROJECT_ID" --location "$REGION" >/dev/null 2>&1 || true

gcloud run worker-pools describe rb-worker-live \
  --project "$PROJECT_ID" --region "$REGION" \
  --format='value(status.conditions[0].type,status.conditions[0].status)'
