#!/bin/zsh
# gcp_bootstrap.zsh — Multiplayer v3 Phase M1, step 0: stand up the Google
# Cloud project, and the $25/mo billing alert, REPRODUCIBLY.
#
# Design doc Multiplayer_V3_Design_Doc.md §7 and §8 (Phase M1): "gcloud setup
# scripts checked into the repo (no console-clicking that can't be reproduced)"
# and "billing alert at $25/mo configured on day one — an idle-spinning Unity
# worker is exactly the thing that quietly eats a budget."
#
# Safe to re-run: every step checks for what it is about to create.
# Creates NOTHING that costs money. No VM, no Cloud SQL instance, no Cloud Run
# service — those come with the code that needs them. This script only makes
# the project, turns on APIs, creates two (empty, free) buckets and an
# Artifact Registry repo, and arms the budget alarm BEFORE anything can spend.
#
# ---------------------------------------------------------------------------
# BEFORE YOU RUN THIS, BY HAND (these need a human and a card):
#   1. brew install --cask google-cloud-sdk
#   2. gcloud auth login          # as leondu167@gmail.com
#   3. Have a billing account. If you have never used GCP on this address, open
#      https://console.cloud.google.com/billing and create one — that is where
#      the $300 / 90-day free credit is applied. Card required; it does not
#      auto-charge when the credit runs out unless you upgrade.
#   4. gcloud billing accounts list        # copy the ACCOUNT_ID it prints
# ---------------------------------------------------------------------------
set -e

# ------------------------------------------------------------------ settings
PROJECT_ID="${PROJECT_ID:-robot-brawl-ladder}"     # must be globally unique
PROJECT_NAME="Robot Brawl Ladder"
REGION="${REGION:-us-central1}"
BUDGET_USD="${BUDGET_USD:-25}"
BILLING_ACCOUNT="${BILLING_ACCOUNT:-}"             # or: BILLING_ACCOUNT=X ./gcp_bootstrap.zsh

if [[ -z "$BILLING_ACCOUNT" ]]; then
  echo "Billing accounts on this login:"
  gcloud billing accounts list
  echo ""
  echo "Re-run with the one you want, e.g.:"
  echo "  BILLING_ACCOUNT=0X0X0X-0X0X0X-0X0X0X zsh $0"
  exit 1
fi

echo "== project ${PROJECT_ID} in ${REGION}, budget \$${BUDGET_USD}/mo =="
echo ""

# ------------------------------------------------------------------- project
if gcloud projects describe "$PROJECT_ID" >/dev/null 2>&1; then
  echo "-- project ${PROJECT_ID} already exists"
else
  gcloud projects create "$PROJECT_ID" --name="$PROJECT_NAME"
fi
gcloud config set project "$PROJECT_ID"

# ------------------------------------------------------------------- billing
# Linked FIRST: nothing else can be enabled without it, and the budget below
# needs the link to exist.
gcloud billing projects link "$PROJECT_ID" --billing-account="$BILLING_ACCOUNT"

# ---------------------------------------------------- the alarm, before spend
# §7: configured on day one. Thresholds at 50% and 90% of current spend, 100%
# of current, and 100% of FORECAST — the forecast rule is the one that warns
# you on day 4 that a worker you left running will blow the month.
if gcloud billing budgets list --billing-account="$BILLING_ACCOUNT" \
      --format="value(displayName)" 2>/dev/null | grep -qx "robot-brawl ${BUDGET_USD}/mo"; then
  echo "-- budget 'robot-brawl ${BUDGET_USD}/mo' already exists"
else
  gcloud billing budgets create \
    --billing-account="$BILLING_ACCOUNT" \
    --display-name="robot-brawl ${BUDGET_USD}/mo" \
    --budget-amount="${BUDGET_USD}USD" \
    --filter-projects="projects/${PROJECT_ID}" \
    --threshold-rule=percent=0.5 \
    --threshold-rule=percent=0.9 \
    --threshold-rule=percent=1.0 \
    --threshold-rule=percent=1.0,basis=forecasted-spend
  echo "-- budget armed; alerts go to the billing account's IAM admins"
  echo "   (leondu167@gmail.com). Verify at:"
  echo "   https://console.cloud.google.com/billing/${BILLING_ACCOUNT}/budgets"
fi

# ---------------------------------------------------------------------- APIs
# Only what §5.1 actually names. No GCP-only service in the critical path
# (§7 portability rule 4) — Cloud Run is just a container host and the queue
# is Postgres, so there is deliberately no Pub/Sub, Firestore or Spanner here.
gcloud services enable \
  run.googleapis.com \
  sqladmin.googleapis.com \
  storage.googleapis.com \
  artifactregistry.googleapis.com \
  compute.googleapis.com \
  monitoring.googleapis.com \
  billingbudgets.googleapis.com

# ------------------------------------------------------------------- buckets
# §5.1: snapshots/ private, replays/ public-read via signed URLs. Uniform
# bucket-level access on both — per-object ACLs are how a "private" bucket
# quietly stops being private.
for B in "${PROJECT_ID}-snapshots" "${PROJECT_ID}-replays"; do
  if gcloud storage buckets describe "gs://${B}" >/dev/null 2>&1; then
    echo "-- bucket gs://${B} already exists"
  else
    gcloud storage buckets create "gs://${B}" \
      --location="$REGION" --uniform-bucket-level-access
  fi
done
echo "-- NOTE: neither bucket is public. Replays are served by SIGNED URL from"
echo "   the API (§5.1); do not add allUsers to the replays bucket."

# --------------------------------------------------------- container registry
if gcloud artifacts repositories describe rb --location="$REGION" >/dev/null 2>&1; then
  echo "-- artifact repo 'rb' already exists"
else
  gcloud artifacts repositories create rb \
    --repository-format=docker --location="$REGION" \
    --description="Robot Brawl API + sim worker images"
fi

echo ""
echo "== done =="
echo "project        ${PROJECT_ID}"
echo "region         ${REGION}"
echo "buckets        gs://${PROJECT_ID}-snapshots  gs://${PROJECT_ID}-replays"
echo "images         ${REGION}-docker.pkg.dev/${PROJECT_ID}/rb"
echo "budget         \$${BUDGET_USD}/mo, alerts at 50/90/100% spend + 100% forecast"
echo ""
echo "Nothing here costs money yet. The first spend will be whichever of these"
echo "you create next: a Cloud SQL instance (~\$10-15/mo) or the worker VM"
echo "(~\$13-25/mo). §7 says: stop the worker VM when idle — the Postgres queue"
echo "just waits."
