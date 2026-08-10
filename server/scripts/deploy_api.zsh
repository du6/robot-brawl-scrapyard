#!/bin/zsh
# Build the API image with Cloud Build and deploy it to Cloud Run.
# Reads its settings from the project gcp_bootstrap.zsh created.
#
# THIS SCRIPT NOW MATCHES A DEPLOYMENT THAT ACTUALLY RAN. The first version
# was written from the docs and never executed; every flag below that carries
# a comment was added because its absence failed a real deploy on 08-09.
set -e
PROJECT_ID="${PROJECT_ID:-robot-brawl-ladder}"
REGION="${REGION:-us-central1}"
SQL_INSTANCE="${SQL_INSTANCE:-${PROJECT_ID}:${REGION}:rb-db}"
IMAGE="${REGION}-docker.pkg.dev/${PROJECT_ID}/rb/api:$(date +%Y%m%d-%H%M%S)"

# The build runs as an explicit service account. New projects no longer get
# the legacy Cloud Build service account, so a plain `builds submit` fails
# with PERMISSION_DENIED even when you hold roles/owner — the error names a
# principal the project does not have. --default-buckets-behavior is required
# alongside it: a user-specified build SA cannot write to the legacy shared
# staging bucket. --region matters too; the build must run where the registry
# is or the push crosses regions.
BUILD_SA="${BUILD_SA:-projects/${PROJECT_ID}/serviceAccounts/$(gcloud projects describe "$PROJECT_ID" --format='value(projectNumber)')-compute@developer.gserviceaccount.com}"

cd "$(dirname "$0")/.."
gcloud builds submit --project "$PROJECT_ID" --region "$REGION" --tag "$IMAGE" \
  --service-account="$BUILD_SA" \
  --default-buckets-behavior=regional-user-owned-bucket \
  .

# --min-instances=0 is the point of Cloud Run here (§7: scales to zero, ~$0 at
# prototype traffic). Secrets come from Secret Manager, never from flags —
# --set-env-vars would put JWT_SECRET in the deployment history.
#
# --add-cloudsql-instances is what mounts the unix socket at
# /cloudsql/<instance>. rb-db has NO public IP, so without this the app boots,
# passes its own health check, and fails on the first query.
#
# The BLOB_S3_* group is not optional in the cloud. Without a bucket the app
# falls back to FileBlobStore, which writes to per-instance disk that vanishes
# when the service scales to zero — uploads return 200 and the bytes are gone.
# GCS is addressed through its S3 interop endpoint with an HMAC key.
gcloud run deploy rb-api \
  --project "$PROJECT_ID" --region "$REGION" \
  --image "$IMAGE" \
  --min-instances=0 --max-instances=4 \
  --cpu=1 --memory=512Mi \
  --allow-unauthenticated \
  --add-cloudsql-instances="$SQL_INSTANCE" \
  --set-env-vars=BLOB_S3_BUCKET=${PROJECT_ID}-snapshots,BLOB_S3_ENDPOINT=https://storage.googleapis.com \
  --set-secrets=JWT_SECRET=rb-jwt-secret:latest,WORKER_KEY=rb-worker-key:latest,PG_CONN=rb-pg-conn:latest,BLOB_S3_KEY=rb-s3-key:latest,BLOB_S3_SECRET=rb-s3-secret:latest

URL=$(gcloud run services describe rb-api --project "$PROJECT_ID" --region "$REGION" --format='value(status.url)')
echo ""
echo "Deployed ${IMAGE}"
echo ""
# NOTE THE TRAILING SLASH. On owen's network `/healthz` is intercepted and
# answers a Google 404 with no `server` header, while `/healthz/` returns the
# app's real 200. That is not Cloud Run and not the app; it cost a session
# once. If this prints 200 the service is up.
echo -n "health: "
curl -s -o /dev/null -w '%{http_code}\n' "${URL}/healthz/"
echo "url:    ${URL}"
