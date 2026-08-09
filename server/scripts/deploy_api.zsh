#!/bin/zsh
# Build the API image with Cloud Build and deploy it to Cloud Run.
# Reads its settings from the project gcp_bootstrap.zsh created.
set -e
PROJECT_ID="${PROJECT_ID:-robot-brawl-ladder}"
REGION="${REGION:-us-central1}"
IMAGE="${REGION}-docker.pkg.dev/${PROJECT_ID}/rb/api:$(date +%Y%m%d-%H%M%S)"

cd "$(dirname "$0")/.."
gcloud builds submit --project "$PROJECT_ID" --tag "$IMAGE" .

# --min-instances=0 is the point of Cloud Run here (§7: scales to zero, ~$0 at
# prototype traffic). Secrets come from Secret Manager, never from flags —
# --set-env-vars would put JWT_SECRET in the deployment history.
gcloud run deploy rb-api \
  --project "$PROJECT_ID" --region "$REGION" \
  --image "$IMAGE" \
  --min-instances=0 --max-instances=4 \
  --cpu=1 --memory=512Mi \
  --allow-unauthenticated \
  --set-secrets=JWT_SECRET=rb-jwt-secret:latest,WORKER_KEY=rb-worker-key:latest,PG_CONN=rb-pg-conn:latest

echo ""
echo "Deployed ${IMAGE}"
echo "Health:  curl \$(gcloud run services describe rb-api --project $PROJECT_ID --region $REGION --format='value(status.url)')/healthz"
