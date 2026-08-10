#!/bin/zsh
# §M4's monitoring/alerting, as a script rather than console clicks — §M1
# requires that nothing about this project's cloud setup exists only as a
# thing somebody once clicked.
#
# Creates, and is safe to re-run:
#   * four log-based metrics extracted from the reaper's "rbmetrics" line
#   * an email notification channel
#   * four alert policies
#   * an uptime check on /healthz/
#
# WHY LOG-BASED AND NOT A REAL METRIC EXPORTER: the app is one Cloud Run
# service with no sidecar, and the numbers are already computed on a timer by
# the reaper. Writing them to stdout costs nothing and needs no credentials.
# The cost is that the extractor is a REGEX over the log text — reword the
# message and every metric here goes flat rather than red. tests/api_smoke.sh
# section M asserts that line's exact shape for that reason.
set -e
PROJECT_ID="${PROJECT_ID:-robot-brawl-ladder}"
REGION="${REGION:-us-central1}"
ALERT_EMAIL="${ALERT_EMAIL:-leondu167@gmail.com}"
API="https://monitoring.googleapis.com/v3/projects/${PROJECT_ID}"
TOKEN() { gcloud auth print-access-token; }

# --------------------------------------------------------------- metrics
# One metric per number we alert on. DELTA/DISTRIBUTION because that is what
# a log-based metric with a value extractor produces. The conditions below
# align with ALIGN_PERCENTILE_99, not ALIGN_MAX: Monitoring refuses MAX on a
# DELTA distribution outright. p99 over a 5-minute window of samples taken
# every 30s is "the worst value seen", which is what these thresholds mean.
mk_metric() {  # <name> <display> <regex-capture> <unit>
  local name="$1" disp="$2" rx="$3" unit="$4"
  cat > "/tmp/rb_m_${name}.yaml" <<EOF
filter: |-
  resource.type="cloud_run_revision"
  resource.labels.service_name="rb-api"
  textPayload:"rbmetrics"
valueExtractor: REGEXP_EXTRACT(textPayload, "${rx}")
metricDescriptor:
  metricKind: DELTA
  valueType: DISTRIBUTION
  unit: "${unit}"
  displayName: ${disp}
bucketOptions:
  exponentialBuckets:
    numFiniteBuckets: 16
    growthFactor: 2
    scale: 1
EOF
  if gcloud logging metrics describe "$name" --project "$PROJECT_ID" >/dev/null 2>&1; then
    gcloud logging metrics update "$name" --config-from-file="/tmp/rb_m_${name}.yaml" --project "$PROJECT_ID" >/dev/null
    echo "  updated $name"
  else
    gcloud logging metrics create "$name" --config-from-file="/tmp/rb_m_${name}.yaml" --project "$PROJECT_ID" >/dev/null
    echo "  created $name"
  fi
}

echo "log-based metrics:"
mk_metric rb_queue_oldest_ready_s     "Oldest READY job age"        'oldest_ready_s=([0-9]+)'      s
mk_metric rb_queue_oldest_heartbeat_s "Oldest worker heartbeat age" 'oldest_heartbeat_s=([0-9]+)'  s
mk_metric rb_queue_failed             "FAILED jobs"                 'failed=([0-9]+)'              1
mk_metric rb_snapshot_oldest_pending_s "Oldest PENDING snapshot age" 'oldest_pending_s=([0-9]+)'   s

# ---------------------------------------------------------------- channel
echo "notification channel:"
CH=$(curl -s -H "Authorization: Bearer $(TOKEN)" "${API}/notificationChannels" \
     | python3 -c "
import json,sys
d=json.load(sys.stdin).get('notificationChannels',[])
print(next((c['name'] for c in d if c.get('labels',{}).get('email_address')=='${ALERT_EMAIL}'), ''))")
if [ -z "$CH" ]; then
  CH=$(curl -s -X POST -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
       "${API}/notificationChannels" -d "{
         \"type\": \"email\",
         \"displayName\": \"owen (ladder alerts)\",
         \"labels\": {\"email_address\": \"${ALERT_EMAIL}\"}
       }" | python3 -c "import json,sys;print(json.load(sys.stdin).get('name',''))")
  echo "  created $CH"
else
  echo "  reusing $CH"
fi
[ -z "$CH" ] && { echo "could not create a notification channel"; exit 1; }

# --------------------------------------------------------------- policies
# ON MISSING DATA. Every queue policy uses EVALUATION_MISSING_DATA_INACTIVE,
# and that is a deliberate choice rather than the default being left alone:
# min-instances=0 means the app is NOT RUNNING whenever nobody is polling, so
# these metrics are absent most of the time by design. Firing on absence would
# page for an idle ladder, every night. The cost is real and worth stating —
# a queue that backs up while nothing polls is invisible to these four. The
# uptime check below is what covers "is it alive", by generating the traffic
# that makes the metrics exist in the first place.
mk_policy() {  # <display> <metric> <threshold> <duration> <doc>
  local disp="$1" metric="$2" thr="$3" dur="$4" doc="$5"
  local existing
  existing=$(curl -s -H "Authorization: Bearer $(TOKEN)" "${API}/alertPolicies" \
    | python3 -c "
import json,sys
d=json.load(sys.stdin).get('alertPolicies',[])
print(next((p['name'] for p in d if p.get('displayName')=='''${disp}'''), ''))")
  cat > /tmp/rb_policy.json <<EOF
{
  "displayName": "${disp}",
  "combiner": "OR",
  "conditions": [{
    "displayName": "${disp}",
    "conditionThreshold": {
      "filter": "metric.type=\"logging.googleapis.com/user/${metric}\" AND resource.type=\"cloud_run_revision\"",
      "aggregations": [{
        "alignmentPeriod": "300s",
        "perSeriesAligner": "ALIGN_PERCENTILE_99",
        "crossSeriesReducer": "REDUCE_MAX"
      }],
      "comparison": "COMPARISON_GT",
      "thresholdValue": ${thr},
      "duration": "${dur}",
      "trigger": {"count": 1},
      "evaluationMissingData": "EVALUATION_MISSING_DATA_INACTIVE"
    }
  }],
  "notificationChannels": ["${CH}"],
  "alertStrategy": {"autoClose": "86400s"},
  "documentation": {"content": "${doc}", "mimeType": "text/markdown"}
}
EOF
  if [ -n "$existing" ]; then
    curl -s -X PATCH -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
      "https://monitoring.googleapis.com/v3/${existing}?updateMask=conditions,notificationChannels,documentation,alertStrategy" \
      -d @/tmp/rb_policy.json >/dev/null
    echo "  updated ${disp}"
  else
    curl -s -X POST -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
      "${API}/alertPolicies" -d @/tmp/rb_policy.json \
      | python3 -c "
import json,sys
d=json.load(sys.stdin)
print('  created' if 'name' in d else '  FAILED: '+json.dumps(d)[:300])"
  fi
}

echo "alert policies:"
# 15 minutes. A VALIDATE normally completes in seconds; this is the threshold
# for 'a job exists and nothing is working it', which is what a dead worker
# fleet looks like from the API's side.
mk_policy "ladder: jobs are not being worked" rb_queue_oldest_ready_s 900 "300s" \
  "A job has been READY for over 15 minutes. Usually means no worker is polling. Check the worker fleet, then GET /v1/admin/metrics with the worker key."
# The reaper returns a job at 300s. Seeing 900 means the reaper itself is not
# running — a different fault from a stuck worker, and it needs the API looked
# at rather than the workers.
mk_policy "ladder: the reaper is not reaping" rb_queue_oldest_heartbeat_s 900 "300s" \
  "A CLAIMED job has gone 15 minutes without a heartbeat. The reaper returns them at 5 minutes, so this means the reaper loop is not running — look at the API, not the workers."
mk_policy "ladder: jobs are failing" rb_queue_failed 5 "600s" \
  "More than 5 FAILED jobs. Check last_error in match_jobs; a payload that cannot be fetched fails every worker that claims it."
# The one a player feels. 30 minutes of 'still checking your robot' is the
# point at which it reads as broken rather than busy.
mk_policy "ladder: uploaded robots are stuck" rb_snapshot_oldest_pending_s 1800 "600s" \
  "A snapshot has been PENDING for over 30 minutes — a player uploaded a robot and it has not been validated. Note this can be non-zero while the job queue is empty and clean, which means a snapshot has no job at all."

# ----------------------------------------------------------- uptime check
# Hits /healthz/ WITH THE TRAILING SLASH — on owen's network the slashless
# form is intercepted and answers a Google 404. That is a local quirk and not
# what Google's probers see, but the check is written the same way so the
# thing being monitored is the thing that gets curl'd by hand.
#
# This also does the work of keeping the metrics alive: each probe wakes an
# instance, which runs a reaper pass, which emits a line. Without it, a
# scale-to-zero service produces no queue metrics at all.
echo "uptime check:"
HOST=$(gcloud run services describe rb-api --project "$PROJECT_ID" --region "$REGION" --format='value(status.url)' | sed 's|https://||')
EXISTING=$(curl -s -H "Authorization: Bearer $(TOKEN)" "${API}/uptimeCheckConfigs" \
  | python3 -c "
import json,sys
d=json.load(sys.stdin).get('uptimeCheckConfigs',[])
print(next((u['name'] for u in d if u.get('displayName')=='rb-api health'), ''))")
if [ -z "$EXISTING" ]; then
  curl -s -X POST -H "Authorization: Bearer $(TOKEN)" -H 'Content-Type: application/json' \
    "${API}/uptimeCheckConfigs" -d "{
      \"displayName\": \"rb-api health\",
      \"monitoredResource\": {\"type\": \"uptime_url\", \"labels\": {\"host\": \"${HOST}\"}},
      \"httpCheck\": {\"path\": \"/healthz/\", \"port\": 443, \"useSsl\": true, \"validateSsl\": true},
      \"period\": \"300s\",
      \"timeout\": \"10s\"
    }" | python3 -c "
import json,sys
d=json.load(sys.stdin)
print('  created for ${HOST}' if 'name' in d else '  FAILED: '+json.dumps(d)[:300])"
else
  echo "  reusing $EXISTING"
fi

echo
echo "done. review at https://console.cloud.google.com/monitoring/alerting?project=${PROJECT_ID}"
